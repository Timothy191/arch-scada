using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Services;

public sealed class SimulatedTelemetryService : ITelemetryService
{
    private readonly Random _rng = new(42);
    private readonly Dictionary<string, List<double>> _history = new();
    private DispatcherTimer? _timer;

    public ObservableCollection<EquipmentItem> Equipment { get; } = new();
    public ObservableCollection<AlarmItem> Alarms { get; } = new();
    public ObservableCollection<TelemetryTag> AllTags { get; } = new();

    public bool IsServerConnected { get; private set; }
    public bool IsOfflineDemoMode { get; private set; } = true;
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
    {
        SeedPlantModel();
        SeedInitialAlarms();
    }

    public void Start(bool offlineDemoMode)
    {
        IsOfflineDemoMode = offlineDemoMode;
        IsServerConnected = !offlineDemoMode;

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
        }
        TelemetryUpdated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<double> GetTagHistory(string tagId)
    {
        return _history.TryGetValue(tagId, out var list)
            ? list.AsReadOnly()
            : Array.Empty<double>();
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
        TelemetryUpdated?.Invoke(this, EventArgs.Empty);
    }
}
