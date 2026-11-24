using ArchScada.CoreIntegration;
using Scada.Data.Const;
using Scada.Data.Models;

namespace ArchScada.PlcSimulator;

/// <summary>
/// Thread-safe simulated 52-channel Arch Coal Preparation Plant (CHPP) field telemetry model.
/// Supplies coupled values across Rapid SCADA native TCP channels, REST tag snapshots, and Modbus TCP registers 0..99.
/// </summary>
public sealed class ChppTelemetryModel
{
    public const int ModbusRegisterCount = 100;

    private readonly record struct ChannelSpec(
        int CnlNum,
        string TagId,
        string EquipmentId,
        string Name,
        string Unit,
        double BaseValue,
        double Amplitude,
        double PhaseOffset,
        double MinValue,
        double MaxValue,
        double ModbusScale);

    private static readonly ChannelSpec[] ChannelSpecs =
    [
        // Line 1 / Device 1: CR-101 Primary Jaw Crusher
        new(101, "CR101_TPH", "CR-101", "CR-101 Crusher Throughput", "t/h", 642.5, 18.0, 0.0, 0.0, 900.0, 10.0),
        new(102, "CR101_KW", "CR-101", "CR-101 Crusher Motor Power", "kW", 315.4, 12.0, 0.2, 0.0, 450.0, 10.0),
        new(103, "CR101_BRG", "CR-101", "CR-101 Bearing Temperature", "°C", 64.2, 1.4, 0.4, 20.0, 95.0, 10.0),

        // Line 1 / Device 2: SC-102 Vibrating Screen 1
        new(111, "SC102_VIB", "SC-102", "SC-102 Screen Vibration", "mm/s", 14.8, 0.4, 0.6, 0.0, 25.0, 100.0),
        new(112, "SC102_KW", "SC-102", "SC-102 Screen Power", "kW", 48.2, 2.0, 0.8, 0.0, 75.0, 10.0),

        // Line 1 / Device 3: CV-101 Main Feed Conveyor
        new(121, "CV101_SPD", "CV-101", "CV-101 Belt Speed", "m/s", 2.85, 0.06, 1.0, 0.0, 4.0, 100.0),
        new(122, "CV101_AMP", "CV-101", "CV-101 Drive Current", "A", 198.4, 8.0, 1.2, 0.0, 280.0, 10.0),

        // Line 1 / Device 4: CV-102 Clean Coal Conveyor
        new(131, "CV102_SPD", "CV-102", "CV-102 Belt Speed", "m/s", 2.60, 0.05, 1.4, 0.0, 4.0, 100.0),
        new(132, "CV102_TMP", "CV-102", "CV-102 Gearbox Temperature", "°C", 58.4, 1.2, 1.6, 20.0, 90.0, 10.0),

        // Line 1 / Device 13: ROM101 ROM Reception & Grizzly Feeder
        new(141, "AF001_TPH", "AF-001", "AF-001 ROM Feed Rate", "t/h", 655.0, 22.0, 0.1, 0.0, 950.0, 10.0),
        new(142, "BN101_LVL", "BN-101", "BN-101 Feeder Bin Level", "%", 68.5, 4.5, 0.3, 0.0, 100.0, 10.0),
        new(143, "VF101_VIB", "VF-101", "VF-101 Grizzly Feeder Vibration", "mm/s", 4.2, 0.25, 0.5, 0.0, 12.0, 100.0),

        // Line 1 / Device 14: DRC101 Double Roller Crusher
        new(151, "DRC101_TPH", "DRC-101", "DRC-101 Double Roll Throughput", "t/h", 530.0, 15.0, 0.7, 0.0, 800.0, 10.0),
        new(152, "DRC101_KW", "DRC-101", "DRC-101 Double Roll Power", "kW", 138.0, 6.0, 0.9, 0.0, 200.0, 10.0),
        new(153, "DRC101_GAP", "DRC-101", "DRC-101 Roll Nip Gap", "mm", 50.0, 0.4, 1.1, 20.0, 80.0, 100.0),

        // Line 1 / Device 15: CV_DRY Crushing & Sizing Conveyors
        new(161, "CV103_SPD", "CV-103", "CV-103 Jaw Discharge Conv Speed", "m/s", 2.55, 0.04, 1.3, 0.0, 4.0, 100.0),
        new(162, "CV104_SPD", "CV-104", "CV-104 Sizing Feed Conv Speed", "m/s", 2.75, 0.05, 1.5, 0.0, 4.0, 100.0),
        new(163, "CV105_SPD", "CV-105", "CV-105 Under-Screen Fines Conv Speed", "m/s", 2.20, 0.04, 1.7, 0.0, 4.0, 100.0),
        new(164, "CV106_SPD", "CV-106", "CV-106 Oversize Return Conv Speed", "m/s", 2.40, 0.04, 1.9, 0.0, 4.0, 100.0),
        new(165, "CV201_SPD", "CV-201", "CV-201 Plant Feed Conv Speed", "m/s", 2.70, 0.05, 2.1, 0.0, 4.0, 100.0),
        new(166, "BN201_LVL", "BN-201", "BN-201 Plant Feed Surge Bin Level", "%", 62.5, 3.0, 2.3, 0.0, 100.0, 10.0),

        // Line 1 / Device 5: TK-201 High-Rate Thickener
        new(201, "TK201_LVL", "TK-201", "TK-201 Thickener Bed Level", "%", 71.5, 2.8, 2.5, 0.0, 100.0, 10.0),
        new(202, "TK201_TRQ", "TK-201", "TK-201 Rake Torque", "%", 44.2, 2.2, 2.7, 0.0, 100.0, 10.0),
        new(203, "TK201_TRB", "TK-201", "TK-201 Overflow Turbidity", "NTU", 18.4, 1.1, 2.9, 0.0, 50.0, 10.0),

        // Line 1 / Devices 6-8: PP-201..203 Thickener Pumps
        new(211, "PP201_FLW", "PP-201", "PP-201 Underflow Flow A", "m³/h", 385.0, 7.5, 3.1, 0.0, 550.0, 10.0),
        new(212, "PP201_PRS", "PP-201", "PP-201 Discharge Pressure", "bar", 4.20, 0.08, 3.3, 0.0, 8.0, 100.0),
        new(221, "PP202_FLW", "PP-202", "PP-202 Standby Underflow Flow B", "m³/h", 0.0, 0.0, 0.0, 0.0, 550.0, 10.0),
        new(231, "PP203_DOS", "PP-203", "PP-203 Flocculant Dosing Rate", "L/min", 24.5, 0.8, 3.5, 0.0, 60.0, 10.0),

        // Line 1 / Device 9: TK-301 Magnetite & DMC Circuit
        new(301, "TK301_SG", "TK-301", "TK-301 Medium Specific Gravity", "SG", 1.52, 0.01, 3.7, 1.1, 2.2, 1000.0),
        new(302, "TK301_LVL", "TK-301", "TK-301 Medium Tank Level", "%", 66.0, 2.5, 3.9, 0.0, 100.0, 10.0),
        new(303, "MS301_REC", "MS-301", "MS-301 Magnetite Recovery", "%", 96.4, 0.4, 4.1, 80.0, 100.0, 10.0),
        new(304, "DM301_FLW", "DM-301", "DM-301 DMC Feed Flow", "m³/h", 812.0, 12.0, 4.3, 0.0, 1100.0, 10.0),
        new(305, "DM301_PRS", "DM-301", "DM-301 Cyclone Inlet Pressure", "kPa", 185.0, 3.5, 4.5, 0.0, 260.0, 10.0),

        // Line 1 / Device 16: DS_WASH Desliming & Wash Conveyors
        new(311, "CV202_SPD", "CV-202", "CV-202 Desliming Feed Conv Speed", "m/s", 2.65, 0.05, 4.7, 0.0, 4.0, 100.0),
        new(312, "DS201_VIB", "DS-201", "DS-201 Desliming Screen Vibration", "mm/s", 3.85, 0.18, 4.9, 0.0, 12.0, 100.0),
        new(313, "DS201_PRS", "DS-201", "DS-201 Desliming Spray Header Pressure", "kPa", 210.0, 4.0, 5.1, 0.0, 300.0, 10.0),
        new(314, "CV203_SPD", "CV-203", "CV-203 Cyclone Feed Conv Speed", "m/s", 2.68, 0.05, 5.3, 0.0, 4.0, 100.0),
        new(315, "CV301_SPD", "CV-301", "CV-301 Clean Product Conv Speed", "m/s", 2.50, 0.04, 5.5, 0.0, 4.0, 100.0),
        new(316, "CV302_SPD", "CV-302", "CV-302 Discard Conv Speed", "m/s", 2.15, 0.04, 5.7, 0.0, 4.0, 100.0),

        // Line 2 / Device 12: CF-501 Fine Coal Centrifuge via OPC UA
        new(501, "CF501_RPM", "CF-501", "CF-501 Basket Speed", "rpm", 680.0, 10.0, 1.8, 0.0, 1000.0, 10.0),
        new(502, "CF501_VIB", "CF-501", "CF-501 Bearing Vibration", "mm/s", 6.40, 0.30, 2.0, 0.0, 20.0, 100.0),
        new(503, "CF501_TORQ", "CF-501", "CF-501 Scroll Differential Torque", "%", 48.0, 1.5, 2.2, 0.0, 100.0, 10.0),
        new(504, "CF501_MST", "CF-501", "CF-501 Product Moisture", "%", 9.60, 0.25, 2.4, 4.0, 18.0, 100.0),

        // Line 2 / Device 11: AN-601 PGNAA Online Analyzer via OPC UA
        new(601, "AN601_ASH", "AN-601", "AN-601 Coal Ash Content", "%", 9.40, 0.14, 2.6, 4.0, 22.0, 100.0),
        new(602, "AN601_MST", "AN-601", "AN-601 Total Moisture", "%", 8.20, 0.10, 2.8, 3.0, 16.0, 100.0),
        new(603, "AN601_SUL", "AN-601", "AN-601 Total Sulfur", "%", 0.62, 0.02, 3.0, 0.1, 2.5, 1000.0),
        new(604, "AN601_CAL", "AN-601", "AN-601 Calorific Value (GCV)", "MJ/kg", 27.80, 0.12, 3.2, 18.0, 32.0, 100.0),

        // Line 3 / Devices 21-23: MQTT Field Dust, Belt-Rip & Env Sensors
        new(701, "ENV_DUST_PM10", "ENV-DUST", "Crusher House Dust PM10", "µg/m³", 14.2, 1.8, 3.4, 0.0, 100.0, 10.0),
        new(702, "ENV_DUST_PM25", "ENV-DUST", "Transfer Tower Dust PM2.5", "µg/m³", 6.1, 0.9, 3.6, 0.0, 50.0, 10.0),
        new(711, "CV101_RIP", "CV-101", "CV-101 Belt-Rip Trip Status", "bool", 0.0, 0.0, 0.0, 0.0, 1.0, 1.0),
        new(712, "CV102_RIP", "CV-102", "CV-102 Belt-Rip Trip Status", "bool", 0.0, 0.0, 0.0, 0.0, 1.0, 1.0),
        new(721, "ENV_CH4_PPM", "ENV-GAS", "Silo Tunnel CH4 Concentration", "ppm", 12.0, 1.5, 3.8, 0.0, 1000.0, 10.0)
    ];

    private readonly object _sync = new();
    private readonly Dictionary<int, double> _values = new(ChannelSpecs.Length);
    private readonly Dictionary<int, ChannelSpec> _specByCnl = ChannelSpecs.ToDictionary(s => s.CnlNum);
    private DateTimeOffset _lastUpdatedUtc = DateTimeOffset.UtcNow;

    public int ChannelCount => ChannelSpecs.Length;

    public ChppTelemetryModel()
    {
        Step(0.0);
    }

    public void Step(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
        {
            elapsedSeconds = 0.0;
        }

        lock (_sync)
        {
            foreach (var spec in ChannelSpecs)
            {
                double wave = spec.Amplitude > 0.0
                    ? Math.Sin(elapsedSeconds * 0.45 + spec.PhaseOffset) * spec.Amplitude
                    : 0.0;
                double val = Math.Clamp(spec.BaseValue + wave, spec.MinValue, spec.MaxValue);
                _values[spec.CnlNum] = Math.Round(val, 3);
            }

            _lastUpdatedUtc = DateTimeOffset.UtcNow;
        }
    }

    public CnlData[] GetChannelDataArray(int[] cnlNums)
    {
        if (cnlNums.Length == 0)
        {
            return [];
        }

        lock (_sync)
        {
            var result = new CnlData[cnlNums.Length];
            for (var i = 0; i < cnlNums.Length; i++)
            {
                if (_values.TryGetValue(cnlNums[i], out var val))
                {
                    result[i] = new CnlData(val, CnlStatusID.Defined);
                }
                else
                {
                    result[i] = CnlData.Empty;
                }
            }

            return result;
        }
    }

    public IReadOnlyList<TagValueSnapshot> GetAllTagSnapshots()
    {
        lock (_sync)
        {
            var list = new List<TagValueSnapshot>(ChannelSpecs.Length);
            foreach (var spec in ChannelSpecs)
            {
                list.Add(new TagValueSnapshot
                {
                    TagId = spec.TagId,
                    Value = _values[spec.CnlNum],
                    TimestampUtc = _lastUpdatedUtc,
                    Quality = "Good"
                });
            }

            return list;
        }
    }

    public PlantConfigurationSnapshot GetPlantConfigSnapshot()
    {
        var areas = new PlantAreaConfig[]
        {
            new() { AreaId = "crushing", Name = "Crushing & Conveyors", Description = "ROM reception, crushing & sizing", SortOrder = 1 },
            new() { AreaId = "thickener", Name = "Thickener Circuit", Description = "High-rate thickener & underflow pumps", SortOrder = 2 },
            new() { AreaId = "magnetite", Name = "Magnetite Preparation", Description = "Desliming, DMC & magnetite recovery", SortOrder = 3 },
            new() { AreaId = "quality", Name = "Quality & Dewatering", Description = "Centrifuge & PGNAA online analyzer", SortOrder = 4 },
            new() { AreaId = "env", Name = "Environmental & Safety", Description = "Dust, belt-rip & CH4 monitoring", SortOrder = 5 }
        };

        var equipment = ChannelSpecs
            .Select(s => s.EquipmentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new EquipmentDefinition
            {
                EquipmentId = id,
                AreaId = "arch-coal-plant",
                Name = id,
                EquipmentType = "CHPP Asset"
            })
            .ToList();

        var tags = ChannelSpecs.Select(s => new TagMetadata
        {
            TagId = s.TagId,
            EquipmentId = s.EquipmentId,
            Name = s.Name,
            Unit = s.Unit,
            RegisterAddress = $"Cnl:{s.CnlNum}",
            MinLimit = s.MinValue,
            MaxLimit = s.MaxValue
        }).ToList();

        return new PlantConfigurationSnapshot
        {
            PlantId = "arch-coal-plant",
            Version = "6.5",
            SavedAtUtc = _lastUpdatedUtc,
            Areas = areas,
            Equipment = equipment,
            Tags = tags
        };
    }

    /// <summary>
    /// Reads 16-bit unsigned Modbus registers in the 0..99 address space.
    /// Registers 0..51 map 1:1 to the 52 CHPP channels (scaled by <c>ModbusScale</c>);
    /// registers 52..99 expose plant-level summary and diagnostic registers.
    /// </summary>
    public ushort[] ReadModbusRegisters(ushort startAddress, ushort count)
    {
        if (startAddress + count > ModbusRegisterCount)
        {
            throw new ArgumentOutOfRangeException(nameof(startAddress));
        }

        lock (_sync)
        {
            var registers = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                int addr = startAddress + i;
                registers[i] = ReadSingleRegisterLocked(addr);
            }

            return registers;
        }
    }

    private ushort ReadSingleRegisterLocked(int address)
    {
        if (address >= 0 && address < ChannelSpecs.Length)
        {
            var spec = ChannelSpecs[address];
            double raw = _values[spec.CnlNum] * spec.ModbusScale;
            return (ushort)Math.Clamp((int)Math.Round(raw), 0, ushort.MaxValue);
        }

        return address switch
        {
            52 => (ushort)ChannelSpecs.Length,
            53 => 1, // Plant running state
            54 => (ushort)Math.Clamp((int)Math.Round(_values[141] * 10.0), 0, ushort.MaxValue), // ROM feed TPH x10
            55 => (ushort)Math.Clamp((int)Math.Round(_values[304] * 8.0), 0, ushort.MaxValue),  // Clean product TPH x10
            56 => (ushort)Math.Clamp((int)Math.Round(_values[301] * 1000.0), 0, ushort.MaxValue), // Medium SG x1000
            57 => (ushort)Math.Clamp((int)Math.Round(_values[601] * 100.0), 0, ushort.MaxValue),  // Coal Ash % x100
            58 => (ushort)Math.Clamp((int)Math.Round(_values[604] * 100.0), 0, ushort.MaxValue),  // GCV MJ/kg x100
            _ => (ushort)(1000 + address)
        };
    }

    public bool HasChannel(int cnlNum) => _specByCnl.ContainsKey(cnlNum);
}
