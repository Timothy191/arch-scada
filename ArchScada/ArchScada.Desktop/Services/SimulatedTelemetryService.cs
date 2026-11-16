using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ArchScada.CoreIntegration;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Services;

public sealed class SimulatedTelemetryService : ITelemetryService
{
    private readonly Random _rng = new(42);
    private readonly Dictionary<string, List<double>> _history = new();
    private DispatcherTimer? _timer;

    public LocalHistorianStore HistorianStore { get; }
    public LocalConfigCache ConfigCache { get; }
    public ScadaServerClient ServerClient { get; }

    public ObservableCollection<EquipmentItem> Equipment { get; } = new();
    public ObservableCollection<AlarmItem> Alarms { get; } = new();
    public ObservableCollection<TelemetryTag> AllTags { get; } = new();

    public bool IsServerConnected { get; private set; }
    public bool IsOfflineDemoMode { get; private set; } = true;
    public string LastServerHealthStatus { get; private set; } = "Not probed (Local Cache Active)";
    public string ConnectionModeLabel => IsServerConnected
        ? "ScadaServer Connected (Read-Only)"
        : "Offline Demo / Local Cache (Read-Only)";

    public double TotalFeedTonnageTph { get; private set; } = 642.5;
    public double TotalPowerDrawKw { get; private set; } = 1485.0;
    public double WaterRecoveryPercent { get; private set; } = 87.4;
    public double MediumSpecificGravity { get; private set; } = 1.52;
    public int ActiveAlarmCount => Alarms.Count;
    public int UnacknowledgedAlarmCount => Alarms.Count(a => !a.IsAcknowledged);
    public int StaleTagCount => AllTags.Count(t => t.IsStale);

    public event EventHandler? TelemetryUpdated;

    public SimulatedTelemetryService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchScada"))
    {
    }

    public SimulatedTelemetryService(string storageDirectory, ScadaServerClient? serverClient = null)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            throw new ArgumentException("Storage directory must not be empty.", nameof(storageDirectory));
        }

        HistorianStore = new LocalHistorianStore(Path.Combine(storageDirectory, "historian"));
        ConfigCache = new LocalConfigCache(Path.Combine(storageDirectory, "plant-config-cache.json"));
        ServerClient = serverClient ?? new ScadaServerClient();

        SeedPlantModel();
        SeedInitialAlarms();
    }

    public void Start(bool offlineDemoMode)
    {
        IsOfflineDemoMode = offlineDemoMode;
        IsServerConnected = false;

        _ = PersistConfigurationSnapshotAsync();
        if (!offlineDemoMode)
        {
            _ = ProbeServerHealthAsync();
        }
        else
        {
            LastServerHealthStatus = "Offline Demo Mode (Local Cache Active)";
        }

        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _timer.Tick += (_, _) => TickSimulation();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public void AcknowledgeAlarm(string alarmId)
    {
        var alarm = Alarms.FirstOrDefault(a => a.Id == alarmId);
        if (alarm is not null && !alarm.IsAcknowledged)
        {
            alarm.IsAcknowledged = true;
            alarm.AcknowledgedAt = DateTime.Now;
            _ = PersistAlarmEventAsync(alarm, "Acknowledged");
            TelemetryUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AcknowledgeAllAlarms()
    {
        var now = DateTime.Now;
        foreach (var alarm in Alarms.Where(a => !a.IsAcknowledged))
        {
            alarm.IsAcknowledged = true;
            alarm.AcknowledgedAt = now;
            _ = PersistAlarmEventAsync(alarm, "Acknowledged");
        }
        TelemetryUpdated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<double> GetTagHistory(string tagId)
    {
        return _history.TryGetValue(tagId, out var list)
            ? list.AsReadOnly()
            : Array.Empty<double>();
    }

    public async Task PersistConfigurationSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var areas = Equipment
                .Select(e => e.Area)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((name, idx) => new PlantAreaConfig
                {
                    AreaId = name.ToLowerInvariant().Replace(" ", "-").Replace("&", "and"),
                    Name = name,
                    Description = $"{name} CHPP process circuit",
                    SortOrder = idx + 1
                })
                .ToList();

            var equipmentDefs = Equipment.Select(e => new EquipmentDefinition
            {
                EquipmentId = e.Id,
                AreaId = e.Area,
                Name = e.Name,
                EquipmentType = e.Type,
                InterlockSummary = e.InterlockSummary
            }).ToList();

            var tagDefs = Equipment.SelectMany(e => e.Tags.Select(t => new TagMetadata
            {
                TagId = t.TagId,
                EquipmentId = e.Id,
                Name = t.Name,
                Unit = t.Unit,
                MinLimit = t.MinLimit,
                MaxLimit = t.MaxLimit
            })).ToList();

            var snapshot = new PlantConfigurationSnapshot
            {
                PlantId = "arch-chpp-01",
                Version = "2.0",
                SavedAtUtc = DateTimeOffset.UtcNow,
                Areas = areas,
                Equipment = equipmentDefs,
                Tags = tagDefs
            };

            await ConfigCache.SaveConfigAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Non-fatal local cache persistence failure in UI/demo mode.
        }
    }

    public async Task PersistCurrentSamplesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var samples = AllTags.Select(t => new TelemetrySample
            {
                TagId = t.TagId,
                Value = t.Value,
                TimestampUtc = nowUtc,
                Quality = t.IsStale ? "Stale" : "Good"
            });

            await HistorianStore.AppendSamplesAsync(samples, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Non-fatal local historian persistence failure.
        }
    }

    public async Task PersistAlarmEventAsync(AlarmItem alarm, string eventType = "Raised", CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = new AlarmJournalEntry
            {
                AlarmId = alarm.Id,
                TimestampUtc = new DateTimeOffset(alarm.Timestamp.ToUniversalTime(), TimeSpan.Zero),
                Severity = alarm.Severity.ToString(),
                AreaId = alarm.Area,
                EquipmentId = alarm.EquipmentId,
                Message = alarm.Message,
                EventType = eventType,
                IsAcknowledged = alarm.IsAcknowledged,
                AcknowledgedAtUtc = alarm.AcknowledgedAt.HasValue
                    ? new DateTimeOffset(alarm.AcknowledgedAt.Value.ToUniversalTime(), TimeSpan.Zero)
                    : null,
                AcknowledgedBy = alarm.IsAcknowledged ? "HMI_OPERATOR" : null
            };

            await HistorianStore.AppendAlarmEventAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Non-fatal local alarm journal persistence failure.
        }
    }

    public async Task<bool> ProbeServerHealthAsync(CancellationToken cancellationToken = default)
    {
        var result = await ServerClient.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Data is not null && result.Data.IsOnline)
        {
            IsServerConnected = true;
            LastServerHealthStatus = $"Online (v{result.Data.ServerVersion}, {result.Data.ActiveChannelCount} channels)";
        }
        else
        {
            IsServerConnected = false;
            LastServerHealthStatus = result.ErrorMessage ?? "ScadaServer unreachable (using local cache)";
        }

        TelemetryUpdated?.Invoke(this, EventArgs.Empty);
        return IsServerConnected;
    }

    private void SeedPlantModel()
    {
        AddEquipment(new EquipmentItem
        {
            Id = "CR-101",
            Name = "Primary Jaw Crusher",
            Area = "Crushing & Conveyors",
            Type = "Crusher",
            Status = EquipmentStatus.Running,
            PowerKw = 315.4,
            LoadPercent = 78.0,
            PrimaryMetric = 642.5,
            PrimaryMetricLabel = "Throughput",
            PrimaryMetricUnit = "t/h",
            Tags =
            {
                CreateTag("CR101_TPH", "Crusher Throughput", 642.5, "t/h", 0, 900),
                CreateTag("CR101_KW", "Crusher Motor Power", 315.4, "kW", 0, 450),
                CreateTag("CR101_BRG", "Bearing Temp", 64.2, "°C", 20, 95)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "SC-102",
            Name = "Vibrating Screen 1",
            Area = "Crushing & Conveyors",
            Type = "Screen",
            Status = EquipmentStatus.Running,
            PowerKw = 48.2,
            LoadPercent = 69.0,
            PrimaryMetric = 14.8,
            PrimaryMetricLabel = "Vibration",
            PrimaryMetricUnit = "mm/s",
            Tags =
            {
                CreateTag("SC102_VIB", "Screen Vibration", 14.8, "mm/s", 0, 25),
                CreateTag("SC102_KW", "Screen Power", 48.2, "kW", 0, 75)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "CV-101",
            Name = "Main Feed Conveyor",
            Area = "Crushing & Conveyors",
            Type = "Conveyor",
            Status = EquipmentStatus.Running,
            PowerKw = 142.0,
            LoadPercent = 74.0,
            PrimaryMetric = 2.85,
            PrimaryMetricLabel = "Belt Speed",
            PrimaryMetricUnit = "m/s",
            TripCascadeTarget = "CR-101",
            Tags =
            {
                CreateTag("CV101_SPD", "Belt Speed", 2.85, "m/s", 0, 4.0),
                CreateTag("CV101_AMP", "Drive Current", 198.4, "A", 0, 280)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "CV-102",
            Name = "Clean Coal Conveyor",
            Area = "Crushing & Conveyors",
            Type = "Conveyor",
            Status = EquipmentStatus.Warning,
            PowerKw = 118.6,
            LoadPercent = 88.0,
            PrimaryMetric = 2.60,
            PrimaryMetricLabel = "Belt Speed",
            PrimaryMetricUnit = "m/s",
            TripCascadeTarget = "SC-102",
            Tags =
            {
                CreateTag("CV102_SPD", "Belt Speed", 2.60, "m/s", 0, 4.0),
                CreateTag("CV102_TMP", "Gearbox Temp", 83.4, "°C", 20, 90)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "TK-201",
            Name = "High-Rate Thickener",
            Area = "Thickener Circuit",
            Type = "Thickener",
            Status = EquipmentStatus.Running,
            PowerKw = 92.0,
            LoadPercent = 62.0,
            PrimaryMetric = 71.5,
            PrimaryMetricLabel = "Bed Level",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("TK201_LVL", "Thickener Level", 71.5, "%", 0, 100),
                CreateTag("TK201_TRQ", "Rake Torque", 44.2, "%", 0, 100),
                CreateTag("TK201_TRB", "Overflow Turbidity", 18.4, "NTU", 0, 50)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "PP-201",
            Name = "Underflow Pump A",
            Area = "Thickener Circuit",
            Type = "Pump",
            Status = EquipmentStatus.Running,
            PowerKw = 110.5,
            LoadPercent = 76.0,
            PrimaryMetric = 385.0,
            PrimaryMetricLabel = "Flow Rate",
            PrimaryMetricUnit = "m³/h",
            Tags =
            {
                CreateTag("PP201_FLW", "Underflow Flow", 385.0, "m³/h", 0, 550),
                CreateTag("PP201_PRS", "Discharge Pressure", 4.2, "bar", 0, 8)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "PP-202",
            Name = "Underflow Pump B (Standby)",
            Area = "Thickener Circuit",
            Type = "Pump",
            Status = EquipmentStatus.Stopped,
            PowerKw = 0.0,
            LoadPercent = 0.0,
            PrimaryMetric = 0.0,
            PrimaryMetricLabel = "Flow Rate",
            PrimaryMetricUnit = "m³/h",
            Tags =
            {
                CreateTag("PP202_FLW", "Standby Flow", 0.0, "m³/h", 0, 550)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "PP-203",
            Name = "Flocculant Dosing Pump",
            Area = "Thickener Circuit",
            Type = "Pump",
            Status = EquipmentStatus.Running,
            PowerKw = 14.2,
            LoadPercent = 54.0,
            PrimaryMetric = 24.5,
            PrimaryMetricLabel = "Dosing",
            PrimaryMetricUnit = "L/min",
            Tags =
            {
                CreateTag("PP203_DOS", "Flocculant Dose", 24.5, "L/min", 0, 60)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "TK-301",
            Name = "Magnetite Medium Tank",
            Area = "Magnetite Preparation",
            Type = "Tank",
            Status = EquipmentStatus.Running,
            PowerKw = 32.0,
            LoadPercent = 66.0,
            PrimaryMetric = 1.52,
            PrimaryMetricLabel = "Density",
            PrimaryMetricUnit = "SG",
            Tags =
            {
                CreateTag("TK301_SG", "Medium Specific Gravity", 1.52, "SG", 1.1, 2.2),
                CreateTag("TK301_LVL", "Medium Tank Level", 66.0, "%", 0, 100)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "MS-301",
            Name = "Magnetic Separator Drum",
            Area = "Magnetite Preparation",
            Type = "Separator",
            Status = EquipmentStatus.Running,
            PowerKw = 76.8,
            LoadPercent = 71.0,
            PrimaryMetric = 96.4,
            PrimaryMetricLabel = "Recovery",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("MS301_REC", "Magnetite Recovery", 96.4, "%", 80, 100)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "DM-301",
            Name = "Dense Medium Cyclone Pump",
            Area = "Magnetite Preparation",
            Type = "Pump",
            Status = EquipmentStatus.Running,
            PowerKw = 265.0,
            LoadPercent = 81.0,
            PrimaryMetric = 812.0,
            PrimaryMetricLabel = "Feed Flow",
            PrimaryMetricUnit = "m³/h",
            Tags =
            {
                CreateTag("DM301_FLW", "DMC Feed Flow", 812.0, "m³/h", 0, 1100),
                CreateTag("DM301_PRS", "Cyclone Inlet Pressure", 185.0, "kPa", 0, 260)
            }
        });

        // --- FINE COAL & FROTH FLOTATION ---
        AddEquipment(new EquipmentItem
        {
            Id = "SP-401",
            Name = "Spiral Bank",
            Area = "Fine Coal & Froth Flotation",
            Type = "Spiral",
            Status = EquipmentStatus.Running,
            PowerKw = 18.5,
            LoadPercent = 68.0,
            PrimaryMetric = 28.4,
            PrimaryMetricLabel = "Feed Solids",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("SP401_SOL", "Spiral Feed Solids", 28.4, "%", 10, 45),
                CreateTag("SP401_FLW", "Spiral Slurry Flow", 310.0, "m³/h", 0, 450)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "FC-401",
            Name = "Flotation Column",
            Area = "Fine Coal & Froth Flotation",
            Type = "FlotationColumn",
            Status = EquipmentStatus.Running,
            PowerKw = 42.0,
            LoadPercent = 73.0,
            PrimaryMetric = 78.5,
            PrimaryMetricLabel = "Pulp Level",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("FC401_LVL", "Column Pulp Level", 78.5, "%", 0, 100),
                CreateTag("FC401_REC", "Combustible Recovery", 86.2, "%", 50, 100)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "AB-401",
            Name = "Flotation Air Blower",
            Area = "Fine Coal & Froth Flotation",
            Type = "Blower",
            Status = EquipmentStatus.Running,
            PowerKw = 88.4,
            LoadPercent = 74.0,
            PrimaryMetric = 1420.0,
            PrimaryMetricLabel = "Airflow",
            PrimaryMetricUnit = "Nm³/h",
            Tags =
            {
                CreateTag("AB401_FLW", "Sparger Airflow", 1420.0, "Nm³/h", 0, 2000),
                CreateTag("AB401_PRS", "Header Pressure", 165.0, "kPa", 0, 250)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "PP-401",
            Name = "Collector Dosing Pump",
            Area = "Fine Coal & Froth Flotation",
            Type = "Pump",
            Status = EquipmentStatus.Running,
            PowerKw = 6.2,
            LoadPercent = 52.0,
            PrimaryMetric = 14.8,
            PrimaryMetricLabel = "Collector Dose",
            PrimaryMetricUnit = "L/min",
            Tags =
            {
                CreateTag("PP401_DOS", "Collector Flow", 14.8, "L/min", 0, 35)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "PP-402",
            Name = "Frother Dosing Pump",
            Area = "Fine Coal & Froth Flotation",
            Type = "Pump",
            Status = EquipmentStatus.Running,
            PowerKw = 4.8,
            LoadPercent = 48.0,
            PrimaryMetric = 9.4,
            PrimaryMetricLabel = "Frother Dose",
            PrimaryMetricUnit = "L/min",
            Tags =
            {
                CreateTag("PP402_DOS", "Frother Flow", 9.4, "L/min", 0, 25)
            }
        });

        // --- DEWATERING & TAILINGS ---
        AddEquipment(new EquipmentItem
        {
            Id = "CF-501",
            Name = "Screen-Bowl Centrifuge",
            Area = "Dewatering & Tailings",
            Type = "Centrifuge",
            Status = EquipmentStatus.Running,
            PowerKw = 195.0,
            LoadPercent = 79.0,
            PrimaryMetric = 9.8,
            PrimaryMetricLabel = "Cake Moisture",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("CF501_MOIST", "Product Cake Moisture", 9.8, "%", 4, 18),
                CreateTag("CF501_AMP", "Bowl Drive Amps", 242.0, "A", 0, 340),
                CreateTag("CF501_VIB", "Frame Vibration", 6.4, "mm/s", 0, 20)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "FP-501",
            Name = "Tailings Filter Press",
            Area = "Dewatering & Tailings",
            Type = "FilterPress",
            Status = EquipmentStatus.Running,
            PowerKw = 64.0,
            LoadPercent = 65.0,
            PrimaryMetric = 12.4,
            PrimaryMetricLabel = "Squeeze Pressure",
            PrimaryMetricUnit = "bar",
            Tags =
            {
                CreateTag("FP501_PRS", "Hydraulic Squeeze Pressure", 12.4, "bar", 0, 18),
                CreateTag("FP501_MOIST", "Tailings Cake Moisture", 19.2, "%", 10, 32)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "CV-501",
            Name = "Tailings Conveyor",
            Area = "Dewatering & Tailings",
            Type = "Conveyor",
            Status = EquipmentStatus.Running,
            PowerKw = 74.5,
            LoadPercent = 61.0,
            PrimaryMetric = 2.15,
            PrimaryMetricLabel = "Belt Speed",
            PrimaryMetricUnit = "m/s",
            TripCascadeTarget = "FP-501",
            Tags =
            {
                CreateTag("CV501_SPD", "Tailings Belt Speed", 2.15, "m/s", 0, 3.5),
                CreateTag("CV501_AMP", "Tailings Drive Amps", 104.2, "A", 0, 180)
            }
        });

        // --- ROM, LOADOUT & PGNAA ---
        AddEquipment(new EquipmentItem
        {
            Id = "AF-001",
            Name = "ROM Apron Feeder",
            Area = "ROM, Loadout & PGNAA",
            Type = "Feeder",
            Status = EquipmentStatus.Running,
            PowerKw = 58.0,
            LoadPercent = 72.0,
            PrimaryMetric = 655.0,
            PrimaryMetricLabel = "ROM Feed Rate",
            PrimaryMetricUnit = "t/h",
            Tags =
            {
                CreateTag("AF001_TPH", "ROM Apron Feed Rate", 655.0, "t/h", 0, 950),
                CreateTag("AF001_SPD", "VFD Speed", 74.0, "%", 0, 100)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "SL-601",
            Name = "Clean Coal Silo",
            Area = "ROM, Loadout & PGNAA",
            Type = "Silo",
            Status = EquipmentStatus.Running,
            PowerKw = 12.0,
            LoadPercent = 64.0,
            PrimaryMetric = 64.5,
            PrimaryMetricLabel = "Silo Level",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("SL601_LVL", "Clean Coal Silo Level", 64.5, "%", 0, 100),
                CreateTag("SL601_TON", "Stored Inventory", 3225.0, "t", 0, 5000)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "AN-601",
            Name = "PGNAA Online Analyzer",
            Area = "ROM, Loadout & PGNAA",
            Type = "Analyzer",
            Status = EquipmentStatus.Running,
            PowerKw = 3.6,
            LoadPercent = 45.0,
            PrimaryMetric = 9.4,
            PrimaryMetricLabel = "Product Ash",
            PrimaryMetricUnit = "%",
            Tags =
            {
                CreateTag("AN601_ASH", "PGNAA Online Ash", 9.4, "%", 4, 22),
                CreateTag("AN601_MOIST", "PGNAA Online Moisture", 8.6, "%", 3, 16),
                CreateTag("AN601_CV", "Calorific Value", 27.8, "MJ/kg", 18, 32)
            }
        });

        AddEquipment(new EquipmentItem
        {
            Id = "LD-601",
            Name = "Train Loadout Batcher",
            Area = "ROM, Loadout & PGNAA",
            Type = "Loadout",
            Status = EquipmentStatus.Running,
            PowerKw = 44.0,
            LoadPercent = 70.0,
            PrimaryMetric = 1850.0,
            PrimaryMetricLabel = "Batch Rate",
            PrimaryMetricUnit = "t/h",
            Tags =
            {
                CreateTag("LD601_TPH", "Loadout Batch Rate", 1850.0, "t/h", 0, 3000),
                CreateTag("LD601_TOT", "Train Batch Total", 4120.0, "t", 0, 8500)
            }
        });
    }

    private void AddEquipment(EquipmentItem item)
    {
        Equipment.Add(item);
        foreach (var tag in item.Tags)
        {
            AllTags.Add(tag);
            var initial = new List<double>(30);
            for (int i = 0; i < 30; i++)
            {
                initial.Add(Math.Round(tag.Value + (_rng.NextDouble() - 0.5) * (tag.MaxLimit - tag.MinLimit) * 0.03, 2));
            }
            _history[tag.TagId] = initial;
        }
    }

    private static TelemetryTag CreateTag(string id, string name, double value, string unit, double min, double max) =>
        new()
        {
            TagId = id,
            Name = name,
            Value = value,
            Unit = unit,
            MinLimit = min,
            MaxLimit = max,
            Timestamp = DateTime.Now
        };

    private void SeedInitialAlarms()
    {
        Alarms.Add(new AlarmItem
        {
            Id = "ALM-1001",
            Timestamp = DateTime.Now.AddMinutes(-12),
            Severity = AlarmSeverity.High,
            Area = "Crushing & Conveyors",
            EquipmentId = "CV-102",
            Message = "CV-102 gearbox temperature approaching high limit (83.4 °C)"
        });
        Alarms.Add(new AlarmItem
        {
            Id = "ALM-1002",
            Timestamp = DateTime.Now.AddMinutes(-27),
            Severity = AlarmSeverity.Medium,
            Area = "Thickener Circuit",
            EquipmentId = "TK-201",
            Message = "TK-201 rake torque elevated above 42% baseline"
        });
        Alarms.Add(new AlarmItem
        {
            Id = "ALM-1003",
            Timestamp = DateTime.Now.AddMinutes(-45),
            Severity = AlarmSeverity.Low,
            Area = "Magnetite Preparation",
            EquipmentId = "TK-301",
            Message = "Magnetite makeup batch #14 completed",
            IsAcknowledged = true,
            AcknowledgedAt = DateTime.Now.AddMinutes(-40)
        });
    }

    private void TickSimulation()
    {
        var now = DateTime.Now;
        double totalPower = 0;

        foreach (var eq in Equipment)
        {
            if (eq.Status == EquipmentStatus.Stopped || eq.Status == EquipmentStatus.Offline)
            {
                continue;
            }

            foreach (var tag in eq.Tags)
            {
                var span = Math.Max(0.5, (tag.MaxLimit - tag.MinLimit) * 0.015);
                var delta = (_rng.NextDouble() - 0.5) * span;
                var next = Math.Clamp(tag.Value + delta, tag.MinLimit, tag.MaxLimit);
                tag.Value = Math.Round(next, 2);
                tag.Timestamp = now;

                if (_history.TryGetValue(tag.TagId, out var series))
                {
                    series.Add(tag.Value);
                    if (series.Count > 60)
                    {
                        series.RemoveAt(0);
                    }
                }
            }

            if (eq.Tags.Count > 0)
            {
                eq.PrimaryMetric = eq.Tags[0].Value;
            }

            eq.PowerKw = Math.Round(Math.Max(0, eq.PowerKw + (_rng.NextDouble() - 0.5) * 4.5), 1);
            eq.LoadPercent = Math.Round(Math.Clamp(eq.LoadPercent + (_rng.NextDouble() - 0.5) * 1.8, 10, 98), 1);
            totalPower += eq.PowerKw;
        }

        TotalPowerDrawKw = Math.Round(totalPower, 1);
        var crusher = Equipment.FirstOrDefault(e => e.Id == "CR-101");
        if (crusher is not null)
        {
            TotalFeedTonnageTph = crusher.PrimaryMetric;
        }

        var magTank = Equipment.FirstOrDefault(e => e.Id == "TK-301");
        if (magTank is not null)
        {
            MediumSpecificGravity = magTank.PrimaryMetric;
        }

        WaterRecoveryPercent = Math.Round(Math.Clamp(WaterRecoveryPercent + (_rng.NextDouble() - 0.5) * 0.3, 80, 95), 1);
        _ = PersistCurrentSamplesAsync();
        TelemetryUpdated?.Invoke(this, EventArgs.Empty);
    }
}
