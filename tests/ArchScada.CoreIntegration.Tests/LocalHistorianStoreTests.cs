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

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
