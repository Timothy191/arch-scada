using ArchScada.CoreIntegration;

namespace ArchScada.CoreIntegration.Tests;

public sealed class LocalConfigCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public LocalConfigCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archscada-config-test-{Guid.NewGuid():N}");
        _cachePath = Path.Combine(_tempDir, "plant-config.json");
    }

    [Fact]
    public async Task LoadConfigAsync_ReturnsNull_WhenCacheDoesNotExist()
    {
        var cache = new LocalConfigCache(_cachePath);

        var loaded = await cache.LoadConfigAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAndLoadConfigAsync_RoundTripsAreasEquipmentAndTagsAcrossInstances()
    {
        var snapshot = new PlantConfigurationSnapshot
        {
            PlantId = "coal-prep-01",
            Version = "3.0.0",
            SavedAtUtc = new DateTimeOffset(2026, 11, 16, 6, 30, 0, TimeSpan.Zero),
            Areas =
            [
                new PlantAreaConfig
                {
                    AreaId = "AREA-CRUSH",
                    Name = "Crushing & Conveyors",
                    Description = "Primary jaw crusher and overland feed conveyors",
                    SortOrder = 1
                },
                new PlantAreaConfig
                {
                    AreaId = "AREA-THICK",
                    Name = "Thickener Circuit",
                    Description = "Clarification and flocculant dosing",
                    SortOrder = 2
                }
            ],
            Equipment =
            [
                new EquipmentDefinition
                {
                    EquipmentId = "CR-101",
                    AreaId = "AREA-CRUSH",
                    Name = "Primary Jaw Crusher",
                    EquipmentType = "Crusher",
                    PlcDeviceId = "PLC-01",
                    InterlockSummary = "Hardware Interlock OK (Read-Only)"
                }
            ],
            Tags =
            [
                new TagMetadata
                {
                    TagId = "CR101_POWER_KW",
                    EquipmentId = "CR-101",
                    Name = "Crusher Motor Power",
                    DataType = "Analog",
                    Unit = "kW",
                    RegisterAddress = "40001",
                    MinLimit = 0,
                    MaxLimit = 500,
                    HighAlarmLimit = 420,
                    HighHighAlarmLimit = 460,
                    StaleTimeoutSeconds = 15
                }
            ]
        };

        var writerCache = new LocalConfigCache(_cachePath);
        await writerCache.SaveConfigAsync(snapshot);

        // Simulate restart with a new instance
        var readerCache = new LocalConfigCache(_cachePath);
        var loaded = await readerCache.LoadConfigAsync();

        Assert.NotNull(loaded);
        Assert.Equal("coal-prep-01", loaded.PlantId);
        Assert.Equal("3.0.0", loaded.Version);
        Assert.Equal(2, loaded.Areas.Count);
        Assert.Equal("AREA-CRUSH", loaded.Areas[0].AreaId);
        Assert.Single(loaded.Equipment);
        Assert.Equal("CR-101", loaded.Equipment[0].EquipmentId);
        Assert.Single(loaded.Tags);
        Assert.Equal("CR101_POWER_KW", loaded.Tags[0].TagId);
        Assert.Equal(460, loaded.Tags[0].HighHighAlarmLimit);
    }

    [Fact]
    public async Task SaveConfigAsync_OverwritesExistingCacheCleanly()
    {
        var cache = new LocalConfigCache(_cachePath);
        await cache.SaveConfigAsync(new PlantConfigurationSnapshot { PlantId = "plant-v1", Version = "1.0" });
        await cache.SaveConfigAsync(new PlantConfigurationSnapshot { PlantId = "plant-v2", Version = "2.0" });

        var loaded = await cache.LoadConfigAsync();

        Assert.NotNull(loaded);
        Assert.Equal("plant-v2", loaded.PlantId);
        Assert.Equal("2.0", loaded.Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
