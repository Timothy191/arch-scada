using ArchScada.CoreIntegration;

namespace ArchScada.CoreIntegration.Tests;

public sealed class LocalHistorianStoreTests : IDisposable
{
    private readonly string _tempDir;

    public LocalHistorianStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archscada-historian-test-{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task AppendAndQuerySamplesAsync_PersistsAcrossInstancesAndFiltersByTagAndTimeRange()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var writer = new LocalHistorianStore(_tempDir);

        await writer.AppendSampleAsync(new TelemetrySample { TagId = "CV101_TPH", Value = 410.5, TimestampUtc = t0, Quality = "Good" });
        await writer.AppendSampleAsync(new TelemetrySample { TagId = "TH201_TORQUE", Value = 32.1, TimestampUtc = t0.AddSeconds(10), Quality = "Good" });
        await writer.AppendSampleAsync(new TelemetrySample { TagId = "CV101_TPH", Value = 425.0, TimestampUtc = t0.AddSeconds(20), Quality = "Good" });
        await writer.AppendSampleAsync(new TelemetrySample { TagId = "CV101_TPH", Value = 430.2, TimestampUtc = t0.AddSeconds(30), Quality = "Stale" });

        // Simulate restart with a fresh store instance
        var reader = new LocalHistorianStore(_tempDir);
        var results = await reader.QuerySamplesAsync(
            tagId: "CV101_TPH",
            fromUtc: t0.AddSeconds(5),
            toUtc: t0.AddSeconds(25));

        Assert.Single(results);
        Assert.Equal(425.0, results[0].Value);
        Assert.Equal("Good", results[0].Quality);
    }

    [Fact]
    public async Task QuerySamplesAsync_OrdersChronologicallyAndAppliesLimit()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        await store.AppendSamplesAsync(
        [
            new TelemetrySample { TagId = "TAG_A", Value = 30, TimestampUtc = t0.AddSeconds(30) },
            new TelemetrySample { TagId = "TAG_A", Value = 10, TimestampUtc = t0.AddSeconds(10) },
            new TelemetrySample { TagId = "TAG_A", Value = 20, TimestampUtc = t0.AddSeconds(20) }
        ]);

        var limited = await store.QuerySamplesAsync(tagId: "TAG_A", limit: 2);

        Assert.Equal(2, limited.Count);
        Assert.Equal(10, limited[0].Value);
        Assert.Equal(20, limited[1].Value);
    }

    [Fact]
    public async Task AppendAndLoadAlarmEventsAsync_PersistsAcrossRestartsAndFiltersCorrectly()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var writer = new LocalHistorianStore(_tempDir);

        await writer.AppendAlarmEventAsync(new AlarmJournalEntry
        {
            AlarmId = "ALM-001",
            TimestampUtc = t0,
            Severity = "Critical",
            AreaId = "AREA-CRUSH",
            EquipmentId = "CR-101",
            TagId = "CR101_POWER_KW",
            Message = "Crusher motor overload",
            EventType = "Raised",
            IsAcknowledged = false
        });

        await writer.AppendAlarmEventAsync(new AlarmJournalEntry
        {
            AlarmId = "ALM-002",
            TimestampUtc = t0.AddMinutes(1),
            Severity = "Medium",
            AreaId = "AREA-THICK",
            EquipmentId = "TH-201",
            Message = "Thickener underflow density drift",
            EventType = "Acknowledged",
            IsAcknowledged = true,
            AcknowledgedAtUtc = t0.AddMinutes(2),
            AcknowledgedBy = "operator1"
        });

        var reader = new LocalHistorianStore(_tempDir);

        var allAlarms = await reader.LoadAlarmEventsAsync();
        Assert.Equal(2, allAlarms.Count);

        var unackCrushAlarms = await reader.LoadAlarmEventsAsync(
            areaId: "AREA-CRUSH",
            unacknowledgedOnly: true);

        Assert.Single(unackCrushAlarms);
        Assert.Equal("ALM-001", unackCrushAlarms[0].AlarmId);
        Assert.Equal("Critical", unackCrushAlarms[0].Severity);
    }

    [Fact]
    public async Task QuerySamplesAsync_SkipsCorruptTrailingLine()
    {
        var store = new LocalHistorianStore(_tempDir);
        await store.AppendSampleAsync(new TelemetrySample
        {
            TagId = "TAG_OK",
            Value = 99.5,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        await File.AppendAllTextAsync(store.SamplesFilePath, "{\"tagId\":\"BROKEN\n");

        var results = await store.QuerySamplesAsync(tagId: "TAG_OK");

        Assert.Single(results);
        Assert.Equal(99.5, results[0].Value);
    }

    [Fact]
    public async Task PruneSamplesAsync_CompactsFileAndKeepsMostRecentSamples()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        Assert.Equal(10_000, store.MaxRetainedSamples);

        for (var i = 1; i <= 6; i++)
        {
            await store.AppendSampleAsync(new TelemetrySample
            {
                TagId = "CR101_TPH",
                Value = 600.0 + i,
                TimestampUtc = t0.AddSeconds(i * 10),
                Quality = "Good"
            });
        }

        var prunedCount = await store.PruneSamplesAsync(maxSamplesToKeep: 3);
        Assert.Equal(3, prunedCount);

        var remaining = await store.QuerySamplesAsync(tagId: "CR101_TPH");
        Assert.Equal(3, remaining.Count);
        Assert.Equal(604.0, remaining[0].Value);
        Assert.Equal(605.0, remaining[1].Value);
        Assert.Equal(606.0, remaining[2].Value);

        var rawLines = (await File.ReadAllLinesAsync(store.SamplesFilePath))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.Equal(3, rawLines.Length);
    }

    [Fact]
    public async Task PruneOlderThanAsync_RemovesSamplesBeforeCutoffAndCompactsFile()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        await store.AppendSamplesAsync(
        [
            new TelemetrySample { TagId = "TK201_LVL", Value = 68.0, TimestampUtc = t0.AddMinutes(-15) },
            new TelemetrySample { TagId = "TK201_LVL", Value = 69.5, TimestampUtc = t0.AddMinutes(-10) },
            new TelemetrySample { TagId = "TK201_LVL", Value = 71.0, TimestampUtc = t0.AddMinutes(-5) },
            new TelemetrySample { TagId = "TK201_LVL", Value = 72.2, TimestampUtc = t0 }
        ]);

        var removed = await store.PruneOlderThanAsync(t0.AddMinutes(-7));
        Assert.Equal(2, removed);

        var remaining = await store.QuerySamplesAsync(tagId: "TK201_LVL");
        Assert.Equal(2, remaining.Count);
        Assert.Equal(71.0, remaining[0].Value);
        Assert.Equal(72.2, remaining[1].Value);
    }

    [Fact]
    public async Task AppendSampleAsync_AutomaticallyEnforcesMaxRetainedSamples()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir, maxRetainedSamples: 4);

        for (var i = 1; i <= 7; i++)
        {
            await store.AppendSampleAsync(new TelemetrySample
            {
                TagId = "AN601_ASH",
                Value = 8.0 + i,
                TimestampUtc = t0.AddSeconds(i),
                Quality = "Good"
            });
        }

        var remaining = await store.QuerySamplesAsync(tagId: "AN601_ASH");
        Assert.Equal(4, remaining.Count);
        Assert.Equal(12.0, remaining[0].Value);
        Assert.Equal(15.0, remaining[3].Value);
    }

    [Fact]
    public async Task AppendSamplesAsync_ConcurrentMultiTagIngestion_PreservesAllSamplesWithoutLossOrCorruption()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir, maxRetainedSamples: 5000);
        string[] tags = ["CR101_TPH", "CR101_KW", "TK201_LVL", "CF501_MST", "AN601_ASH"];

        const int tasksCount = 10;
        const int samplesPerTask = 50;
        var tasks = new List<Task>();

        for (var taskId = 0; taskId < tasksCount; taskId++)
        {
            var currentTaskId = taskId;
            tasks.Add(Task.Run(async () =>
            {
                var batch = new List<TelemetrySample>();
                for (var i = 0; i < samplesPerTask; i++)
                {
                    var tag = tags[(currentTaskId * samplesPerTask + i) % tags.Length];
                    batch.Add(new TelemetrySample
                    {
                        TagId = tag,
                        Value = 100.0 + (currentTaskId * samplesPerTask) + i,
                        TimestampUtc = t0.AddMilliseconds((currentTaskId * samplesPerTask + i) * 10),
                        Quality = "Good"
                    });
                }
                await store.AppendSamplesAsync(batch);
            }));
        }

        await Task.WhenAll(tasks);

        // Verify across a new reader instance (offline persistence verification)
        var reader = new LocalHistorianStore(_tempDir, maxRetainedSamples: 5000);
        var allSamples = await reader.QuerySamplesAsync(limit: 1000);
        Assert.Equal(tasksCount * samplesPerTask, allSamples.Count);

        // Verify per-tag distribution and ordering
        foreach (var tag in tags)
        {
            var tagSamples = await reader.QuerySamplesAsync(tagId: tag, limit: 1000);
            Assert.Equal((tasksCount * samplesPerTask) / tags.Length, tagSamples.Count);

            for (var i = 1; i < tagSamples.Count; i++)
            {
                Assert.True(tagSamples[i].TimestampUtc >= tagSamples[i - 1].TimestampUtc,
                    $"Samples for tag '{tag}' are not chronologically ordered.");
            }
        }
    }

    [Fact]
    public async Task AppendSampleAsync_ConcurrentMixedSamplesAndAlarms_DoesNotDeadlockOrCorrupt()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir, maxRetainedSamples: 2000);

        const int writerCount = 6;
        const int itemsPerWriter = 30;
        var tasks = new List<Task>();

        for (var w = 0; w < writerCount; w++)
        {
            var writerId = w;
            if (writerId % 2 == 0)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (var i = 0; i < itemsPerWriter; i++)
                    {
                        await store.AppendSampleAsync(new TelemetrySample
                        {
                            TagId = "CV101_TPH",
                            Value = 200.0 + i,
                            TimestampUtc = t0.AddSeconds(writerId * 100 + i),
                            Quality = "Good"
                        });
                    }
                }));
            }
            else
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (var i = 0; i < itemsPerWriter; i++)
                    {
                        await store.AppendAlarmEventAsync(new AlarmJournalEntry
                        {
                            AlarmId = $"ALM-{writerId}-{i}",
                            TimestampUtc = t0.AddSeconds(writerId * 100 + i),
                            Severity = "Warning",
                            AreaId = "AREA-CRUSH",
                            EquipmentId = "CR-101",
                            Message = $"Motor temp high warning {i}",
                            EventType = "Raised",
                            IsAcknowledged = false
                        });
                    }
                }));
            }
        }

        await Task.WhenAll(tasks);

        var sampleWriters = writerCount / 2 + (writerCount % 2);
        var alarmWriters = writerCount / 2;

        var samples = await store.QuerySamplesAsync(limit: 1000);
        Assert.Equal(sampleWriters * itemsPerWriter, samples.Count);

        var alarms = await store.LoadAlarmEventsAsync();
        Assert.Equal(alarmWriters * itemsPerWriter, alarms.Count);
    }

    [Fact]
    public async Task QuerySamplesAsync_ExactBoundaryTimestamps_AreInclusive()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        await store.AppendSamplesAsync(
        [
            new TelemetrySample { TagId = "TAG1", Value = 10, TimestampUtc = t0.AddSeconds(10) },
            new TelemetrySample { TagId = "TAG1", Value = 20, TimestampUtc = t0.AddSeconds(20) },
            new TelemetrySample { TagId = "TAG1", Value = 30, TimestampUtc = t0.AddSeconds(30) },
            new TelemetrySample { TagId = "TAG1", Value = 40, TimestampUtc = t0.AddSeconds(40) }
        ]);

        var exactRange = await store.QuerySamplesAsync(
            tagId: "TAG1",
            fromUtc: t0.AddSeconds(20),
            toUtc: t0.AddSeconds(30));

        Assert.Equal(2, exactRange.Count);
        Assert.Equal(20, exactRange[0].Value);
        Assert.Equal(30, exactRange[1].Value);

        var pointInTime = await store.QuerySamplesAsync(
            tagId: "TAG1",
            fromUtc: t0.AddSeconds(20),
            toUtc: t0.AddSeconds(20));

        Assert.Single(pointInTime);
        Assert.Equal(20, pointInTime[0].Value);
    }

    [Fact]
    public async Task QuerySamplesAsync_InvertedOrDisjointTimeRange_ReturnsEmpty()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        await store.AppendSampleAsync(new TelemetrySample { TagId = "TAG_DISJOINT", Value = 100, TimestampUtc = t0 });

        var inverted = await store.QuerySamplesAsync(
            tagId: "TAG_DISJOINT",
            fromUtc: t0.AddSeconds(50),
            toUtc: t0.AddSeconds(10));
        Assert.Empty(inverted);

        var disjointBefore = await store.QuerySamplesAsync(
            tagId: "TAG_DISJOINT",
            fromUtc: t0.AddMinutes(-10),
            toUtc: t0.AddMinutes(-5));
        Assert.Empty(disjointBefore);

        var disjointAfter = await store.QuerySamplesAsync(
            tagId: "TAG_DISJOINT",
            fromUtc: t0.AddMinutes(5),
            toUtc: t0.AddMinutes(10));
        Assert.Empty(disjointAfter);
    }

    [Fact]
    public async Task QuerySamplesAsync_TagIdMatching_IsCaseInsensitive()
    {
        var store = new LocalHistorianStore(_tempDir);
        await store.AppendSampleAsync(new TelemetrySample
        {
            TagId = "Cr101_Tph",
            Value = 555.5,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        var matchUpper = await store.QuerySamplesAsync(tagId: "CR101_TPH");
        var matchLower = await store.QuerySamplesAsync(tagId: "cr101_tph");
        var matchMixed = await store.QuerySamplesAsync(tagId: "cR101_TpH");
        var nonExistent = await store.QuerySamplesAsync(tagId: "NON_EXISTENT_TAG");

        Assert.Single(matchUpper);
        Assert.Single(matchLower);
        Assert.Single(matchMixed);
        Assert.Empty(nonExistent);
    }

    [Fact]
    public async Task LoadAlarmEventsAsync_FiltersByAreaEquipmentAndUnacknowledgedStatus()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        await store.AppendAlarmEventAsync(new AlarmJournalEntry
        {
            AlarmId = "ALM-1",
            TimestampUtc = t0,
            Severity = "Critical",
            AreaId = "AREA-CRUSH",
            EquipmentId = "CR-101",
            Message = "Crusher stall",
            EventType = "Raised",
            IsAcknowledged = false
        });

        await store.AppendAlarmEventAsync(new AlarmJournalEntry
        {
            AlarmId = "ALM-2",
            TimestampUtc = t0.AddMinutes(1),
            Severity = "Warning",
            AreaId = "AREA-CRUSH",
            EquipmentId = "CR-101",
            Message = "Crusher high vibration",
            EventType = "Acknowledged",
            IsAcknowledged = true,
            AcknowledgedAtUtc = t0.AddMinutes(2),
            AcknowledgedBy = "operator1"
        });

        await store.AppendAlarmEventAsync(new AlarmJournalEntry
        {
            AlarmId = "ALM-3",
            TimestampUtc = t0.AddMinutes(3),
            Severity = "High",
            AreaId = "AREA-THICK",
            EquipmentId = "TH-201",
            Message = "Torque high",
            EventType = "Raised",
            IsAcknowledged = false
        });

        var crushUnack = await store.LoadAlarmEventsAsync(areaId: "AREA-CRUSH", unacknowledgedOnly: true);
        Assert.Single(crushUnack);
        Assert.Equal("ALM-1", crushUnack[0].AlarmId);

        var crushAll = await store.LoadAlarmEventsAsync(equipmentId: "CR-101");
        Assert.Equal(2, crushAll.Count);

        var thickener = await store.LoadAlarmEventsAsync(areaId: "AREA-THICK");
        Assert.Single(thickener);
        Assert.Equal("TH-201", thickener[0].EquipmentId);

        var timeWindow = await store.LoadAlarmEventsAsync(
            fromUtc: t0.AddSeconds(30),
            toUtc: t0.AddMinutes(2));
        Assert.Single(timeWindow);
        Assert.Equal("ALM-2", timeWindow[0].AlarmId);
    }

    [Fact]
    public async Task PruneSamplesAsync_WhenCountBelowThreshold_PerformsNoPruning()
    {
        var store = new LocalHistorianStore(_tempDir);
        for (var i = 0; i < 5; i++)
        {
            await store.AppendSampleAsync(new TelemetrySample
            {
                TagId = "TAG_A",
                Value = i,
                TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }

        var pruned = await store.PruneSamplesAsync(maxSamplesToKeep: 10);
        Assert.Equal(0, pruned);

        var remaining = await store.QuerySamplesAsync();
        Assert.Equal(5, remaining.Count);
    }

    [Fact]
    public async Task PruneSamplesAsync_WithZeroMaxSamplesToKeep_ClearsAllSamples()
    {
        var store = new LocalHistorianStore(_tempDir);
        for (var i = 0; i < 5; i++)
        {
            await store.AppendSampleAsync(new TelemetrySample
            {
                TagId = "TAG_CLEAR",
                Value = i,
                TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }

        var pruned = await store.PruneSamplesAsync(maxSamplesToKeep: 0);
        Assert.Equal(5, pruned);

        var remaining = await store.QuerySamplesAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task PruneOlderThanAsync_CutoffOutsideRange_HandlesAllOrNone()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var store = new LocalHistorianStore(_tempDir);

        await store.AppendSamplesAsync(
        [
            new TelemetrySample { TagId = "TAG_CUTOFF", Value = 1, TimestampUtc = t0 },
            new TelemetrySample { TagId = "TAG_CUTOFF", Value = 2, TimestampUtc = t0.AddMinutes(10) },
            new TelemetrySample { TagId = "TAG_CUTOFF", Value = 3, TimestampUtc = t0.AddMinutes(20) }
        ]);

        var prunedNone = await store.PruneOlderThanAsync(t0.AddMinutes(-5));
        Assert.Equal(0, prunedNone);
        Assert.Equal(3, (await store.QuerySamplesAsync()).Count);

        var prunedAll = await store.PruneOlderThanAsync(t0.AddHours(1));
        Assert.Equal(3, prunedAll);
        Assert.Empty(await store.QuerySamplesAsync());
    }

    [Fact]
    public async Task LocalHistorianStore_ValidationConstraints_EnforceExpectedExceptions()
    {
        Assert.Throws<ArgumentException>(() => new LocalHistorianStore(""));
        Assert.Throws<ArgumentException>(() => new LocalHistorianStore("   "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalHistorianStore(_tempDir, maxRetainedSamples: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalHistorianStore(_tempDir, maxRetainedSamples: -5));

        var store = new LocalHistorianStore(_tempDir);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.MaxRetainedSamples = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.MaxRetainedSamples = -10);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.PruneSamplesAsync(-1));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AppendSampleAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AppendSamplesAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AppendAlarmEventAsync(null!));
    }

    [Fact]
    public async Task OfflineBuffering_RapidRestartsAndInterleavedAppends_MaintainsDataContinuity()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

        const int iterations = 5;
        const int samplesPerBatch = 10;
        var expectedTotal = iterations * samplesPerBatch;

        for (var step = 0; step < iterations; step++)
        {
            // Simulate restart by creating fresh store instance pointing to same offline folder
            var currentStore = new LocalHistorianStore(_tempDir);
            var batch = new List<TelemetrySample>();

            for (var i = 0; i < samplesPerBatch; i++)
            {
                batch.Add(new TelemetrySample
                {
                    TagId = "OFFLINE_BUFFER_TAG",
                    Value = (step * 100) + i,
                    TimestampUtc = t0.AddSeconds((step * samplesPerBatch) + i),
                    Quality = "Buffered"
                });
            }

            await currentStore.AppendSamplesAsync(batch);
        }

        // Fresh instance reading back offline accumulated buffer
        var finalStore = new LocalHistorianStore(_tempDir);
        var loaded = await finalStore.QuerySamplesAsync(tagId: "OFFLINE_BUFFER_TAG", limit: 1000);

        Assert.Equal(expectedTotal, loaded.Count);
        for (var i = 0; i < expectedTotal; i++)
        {
            var expectedTimestamp = t0.AddSeconds(i);
            Assert.Equal(expectedTimestamp, loaded[i].TimestampUtc);
            Assert.Equal("Buffered", loaded[i].Quality);
        }
    }

    [Fact]
    public async Task OfflineBuffering_RecoversCleanlyFromInterleavedWhitespaceAndCorruptLines()
    {
        var store = new LocalHistorianStore(_tempDir);
        var t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

        await store.AppendSampleAsync(new TelemetrySample { TagId = "RECOVER_TAG", Value = 1.0, TimestampUtc = t0 });
        await store.AppendSampleAsync(new TelemetrySample { TagId = "RECOVER_TAG", Value = 2.0, TimestampUtc = t0.AddSeconds(1) });

        // Simulate crash during write leaving partial line and blank lines in offline storage
        await File.AppendAllTextAsync(store.SamplesFilePath, "\n   \n{\"tagId\":\"RECOVER_TAG\",\"value\":INVALID_JSON\n\n");

        await store.AppendSampleAsync(new TelemetrySample { TagId = "RECOVER_TAG", Value = 3.0, TimestampUtc = t0.AddSeconds(2) });

        var results = await store.QuerySamplesAsync(tagId: "RECOVER_TAG");
        Assert.Equal(3, results.Count);
        Assert.Equal(1.0, results[0].Value);
        Assert.Equal(2.0, results[1].Value);
        Assert.Equal(3.0, results[2].Value);

        // Compacting/pruning cleanses the file of corruption while keeping valid samples
        await store.PruneSamplesAsync(maxSamplesToKeep: 10);
        var compactedResults = await store.QuerySamplesAsync(tagId: "RECOVER_TAG");
        Assert.Equal(3, compactedResults.Count);

        var lines = (await File.ReadAllLinesAsync(store.SamplesFilePath))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public async Task OfflineBuffering_CreatesStorageDirectoryAutomatically()
    {
        var nestedDir = Path.Combine(_tempDir, "offline", "subfolder", "historian");
        Assert.False(Directory.Exists(nestedDir));

        var store = new LocalHistorianStore(nestedDir);
        await store.AppendSampleAsync(new TelemetrySample
        {
            TagId = "AUTO_DIR_TAG",
            Value = 42.0,
            TimestampUtc = DateTimeOffset.UtcNow
        });

        Assert.True(Directory.Exists(nestedDir));
        Assert.True(File.Exists(store.SamplesFilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
