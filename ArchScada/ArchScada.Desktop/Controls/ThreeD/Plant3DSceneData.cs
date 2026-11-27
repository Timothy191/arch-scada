using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls.ThreeD;

public enum FlowMedium
{
    Coal,
    Slurry,
    Froth,
    Magnetite
}

public enum CoalMaterialClass
{
    RawRom,
    ScalpedUndersize,
    CrushedPrimary,
    ScreenOversize,
    CrushedRoll,
    SizedFines,
    PlantFeed,
    DeslimedCoal,
    CleanProduct,
    RefuseDiscard
}

public sealed record Plant3DNode(
    string Id,
    string GaLabel,
    string StageLabel,
    string EquipmentType,
    string GlbFileName,
    Vector3 Position,
    float YawDegrees,
    Vector3 Scale,
    Vector3 HalfExtents,
    float LabelHeight,
    Color DefaultColor,
    string DimensionsSpec,
    string MechanicalPrinciple,
    string ComponentDescription,
    string[] UpstreamIds,
    string[] DownstreamIds,
    string LinkedScadaId,
    bool HasFillLevel = false,
    float FillRadius = 0f,
    float FillBaseY = 0f,
    float FillMaxHeight = 0f,
    bool Vibrates = false,
    bool HasRotatingTop = false,
    float RotatingTopY = 0f,
    float RotatingTopLength = 0f,
    bool IsConveyor = false,
    bool IsSubComponent = false,
    bool IsPipe = false)
{
    public string UpstreamDisplay => UpstreamIds.Length == 0 ? "ROM Source" : string.Join(", ", UpstreamIds);
    public string DownstreamDisplay => DownstreamIds.Length == 0 ? "Final Stockpile / Product" : string.Join(", ", DownstreamIds);
    public string FlowSummary => $"{UpstreamDisplay}  →  {Id}  →  {DownstreamDisplay}";
    public string PlcChannelMapping => Plant3DSceneData.GetPlcChannelMapping(Id, LinkedScadaId);
    public string ProvisionalConfigNote => Plant3DSceneData.FormatProvisionalConfigNote(this, ChppEngineeringConfig.Current);
}

/// <summary>
/// §5 Enhanced machine node with LOD vertex budgets and PBR material assignment.
/// Includes shadow casting/receiving flags and silhouette weight for intro animation.
/// </summary>
public sealed record EnhancedMachineNode(
    string Id,
    string GaLabel,
    string StageLabel,
    string EquipmentType,
    string GlbFileName,
    Vector3 Position,
    float YawDegrees,
    Vector3 Scale,
    Vector3 HalfExtents,
    float LabelHeight,
    Color DefaultColor,
    string DimensionsSpec,
    string MechanicalPrinciple,
    string ComponentDescription,
    string[] UpstreamIds,
    string[] DownstreamIds,
    string LinkedScadaId,
    bool HasFillLevel = false,
    float FillRadius = 0f,
    float FillBaseY = 0f,
    float FillMaxHeight = 0f,
    bool Vibrates = false,
    bool HasRotatingTop = false,
    float RotatingTopY = 0f,
    float RotatingTopLength = 0f,
    bool IsConveyor = false,
    bool IsSubComponent = false,
    bool IsPipe = false,
    int LODVertexCount = 0,
    string LODTier = "LOD2",
    PbrMaterial? AssignedMaterial = null,
    bool CastShadows = true,
    bool ReceiveShadows = true,
    float SilhouetteWeight = 1.0f);

/// <summary>
/// §5 SubEquipmentNode with detail specs for plant equipment sub-components.
/// Used for detailed viewport rendering and silhouette enhancement.
/// </summary>
public sealed record SubEquipmentNode(
    string Id,
    string ParentEquipmentId,
    string GaLabel,
    string EquipmentSubType,
    Vector3 Position,
    Vector3 HalfExtents,
    string GlbFileName = "",
    PbrMaterial? AssignedMaterial = null,
    bool CastShadows = true,
    bool ReceiveShadows = true,
    float SilhouetteWeight = 1.0f,
    float DetailLevel = 1.0f);

public sealed record ConveyorSpec(
    string Id,
    string GaLabel,
    string StageLabel,
    Vector3 TailPos,
    Vector3 HeadPos,
    float Width,
    float HeadDropTargetY,
    CoalMaterialClass MaterialClass,
    string LinkedScadaId,
    string[] UpstreamIds,
    string[] DownstreamIds,
    string DimensionsSpec,
    string MechanicalPrinciple,
    string ComponentDescription,
    Vector3[]? FeedChuteWaypoints = null,
    Vector3? HeadDischargeOffset = null,
    // §6 Belt texture animation: scrolling UV offsets and rate; HasFlangeDetails renders end flanges.
    float BeltUOffset = 0f,
    float BeltVOffset = 0f,
    float BeltSpeed = 1.0f,
    bool HasFlangeDetails = false)
{
    public Vector3[] GetBeltWaypoints() => new[] { TailPos, HeadPos };

    public Vector3 GetChuteBottomPos()
    {
        var off = HeadDischargeOffset ?? Vector3.Zero;
        return new Vector3(HeadPos.X + off.X, HeadDropTargetY, HeadPos.Z + off.Z);
    }

    public Vector3[] GetFullFlowWaypoints()
    {
        var list = new List<Vector3>(6);
        if (FeedChuteWaypoints is not null && FeedChuteWaypoints.Length > 0)
            list.AddRange(FeedChuteWaypoints);
        list.Add(TailPos);
        list.Add(HeadPos);
        list.Add(GetChuteBottomPos());
        return list.ToArray();
    }
}

public sealed record FoundationPad(
    string ZoneName,
    Vector3 Center,
    Vector3 Size,
    Color AccentColor);

public sealed record PipeRoute(
    string SourceEquipmentId,
    FlowMedium Medium,
    float Radius,
    Vector3[] Waypoints,
    string Id = "",
    string GaLabel = "",
    string StageLabel = "Wash Plant & Medium Recovery",
    string DimensionsSpec = "",
    string MechanicalPrinciple = "",
    string ComponentDescription = "",
    string[]? UpstreamIds = null,
    string[]? DownstreamIds = null);

/// <summary>
/// §5 Enhanced pipe route with beveled joint geometry and flange details at connections.
/// </summary>
public sealed record EnhancedPipeRoute(
    string SourceEquipmentId,
    FlowMedium Medium,
    float Radius,
    Vector3[] Waypoints,
    string Id = "",
    string GaLabel = "",
    string StageLabel = "",
    string DimensionsSpec = "",
    string MechanicalPrinciple = "",
    string ComponentDescription = "",
    string[]? UpstreamIds = null,
    string[]? DownstreamIds = null,
    // LOD and geometry enhancements per §5
    int LODVertexCount = 0,
    bool HasBeveledJoints = false,
    bool HasFlangeDetails = false,
    string PipeMaterial = "Steel");

public readonly record struct Ray3D(Vector3 Origin, Vector3 Direction);

public readonly record struct LoaderPose(
    Vector3 ChassisPosition,
    float YawDegrees,
    float PitchDegrees,
    float BoomAngleDegrees,
    float BucketDumpAngleDegrees,
    float CyclePhase,
    bool IsDumpingCoal,
    float DumpIntensity,
    float BinFillPulse);

public sealed record ProcessFlowState(
    string NodeId,
    double NormalizedRate,
    bool IsRunning,
    bool IsStarved,
    double EstimatedTph);

public readonly record struct SoftParticleInstance(
    Vector3 Position,
    Vector3 Scale,
    float R,
    float G,
    float B,
    float Alpha,
    bool IsWaterSpray);

public sealed record Plant3DGlbMeshMetric(
    string FileName,
    long FileSizeBytes,
    int VertexCount,
    int TriangleCount,
    long VboBytes);

public sealed record Plant3DPerformanceMetrics(
    IReadOnlyList<Plant3DGlbMeshMetric> Meshes,
    int TotalGlbCount,
    int TotalVertices,
    int TotalTriangles,
    double TotalGpuVboMiB,
    double ColdGlbLoadMs,
    double FlowAndPoseMicrosPerFrame,
    long ManagedMemoryDeltaBytes);

/// <summary>
/// Shared 3D CHPP plant scene definition aligned to the General Arrangement (GA) drawing
/// across Stages 1–6 and the Wash Plant / Medium Recovery circuit.
/// Coordinate convention: X = West(-) to East(+), Y = Up >= 0, Z = North(-) to South(+).
/// </summary>
/// <summary>§5 Mesh quality targets: LOD vertex budgets per equipment category.</summary>
public sealed record LodVertexBudgets(
    int LOD1Hero,
    int LOD2Standard,
    int LOD3Background);

/// <summary>§6 PBR material definition (linear sRGB base color; roughness/metallic are non-color).</summary>
public sealed record PbrMaterial(
    string Name,
    Color BaseColor,
    float Roughness,
    float Metallic,
    string? NormalMapOverride = null);

/// <summary>
/// §5/§6 Mesh-quality targets and PBR material library shared by the 3D pipeline.
/// </summary>
public static class PlantMeshQuality
{
    public static readonly LodVertexBudgets VertexBudgets = new(15000, 8000, 2500);
    public static readonly LodVertexBudgets VesselBudgets = new(12000, 6000, 2000);
    public static readonly LodVertexBudgets PumpPipingBudgets = new(8000, 4000, 1500);
    public static readonly LodVertexBudgets ValveFittingBudgets = new(4000, 2000, 800);

    public static readonly Dictionary<string, LodVertexBudgets> EquipmentCategoryBudgets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Stockpile"] = VesselBudgets,
        ["WheelLoader"] = PumpPipingBudgets,
        ["ReceivingHopper"] = VesselBudgets,
        ["VibratingGrizzlyFeeder"] = PumpPipingBudgets,
        ["JawCrusher"] = ValveFittingBudgets,
        ["DoubleRollCrusher"] = ValveFittingBudgets,
        ["SizingScreen"] = ValveFittingBudgets,
        ["SurgeBin"] = VesselBudgets,
        ["DeslimingScreen"] = ValveFittingBudgets,
        ["WashPlantModule"] = ValveFittingBudgets,
        ["MagneticSeparator"] = ValveFittingBudgets,
        ["MediumTank"] = VesselBudgets,
        ["Thickener"] = VesselBudgets,
        ["CentrifugalPump"] = PumpPipingBudgets,
        ["ProductStockpile"] = VesselBudgets,
        ["DiscardStockpile"] = VesselBudgets,
    };

    public static LodVertexBudgets GetEquipmentBudget(string equipmentType) =>
        EquipmentCategoryBudgets.TryGetValue(equipmentType, out var budget) ? budget : VertexBudgets;

    public static string CalculateLODTier(int vertexCount, string equipmentType)
    {
        var budget = GetEquipmentBudget(equipmentType);
        if (vertexCount <= budget.LOD3Background) return "LOD3";
        if (vertexCount <= budget.LOD2Standard) return "LOD2";
        return "LOD1";
    }

    // §6 PBR material library (linear sRGB authoring).
    public static readonly PbrMaterial Steel = new("Steel", Color.Parse("#71717A"), 0.35f, 0.85f);
    public static readonly PbrMaterial CeramicLined = new("Ceramic-Lined", Color.Parse("#E5E7E8"), 0.45f, 0.20f);
    public static readonly PbrMaterial RubberLined = new("Rubber-Lined", Color.Parse("#F2D9CE"), 0.60f, 0.10f);
    public static readonly PbrMaterial Coal = new("Coal", Color.Parse("#2D2D2D"), 0.90f, 0.05f);
    public static readonly PbrMaterial Magnetite = new("Magnetite", Color.Parse("#1E1E1E"), 0.40f, 0.95f);
    public static readonly PbrMaterial Water = new("Water", Color.Parse("#A0E7FF"), 0.10f, 0.05f);
    public static readonly PbrMaterial Plastic = new("Plastic", Color.Parse("#FBBF24"), 0.55f, 0.30f);
    public static readonly PbrMaterial Glass = new("Glass", Color.Parse("#F8FAFF"), 0.20f, 1.00f);

    public static PbrMaterial GetPbrMaterial(string materialName) => materialName switch
    {
        "Steel" => Steel,
        "Ceramic-Lined" => CeramicLined,
        "Rubber-Lined" => RubberLined,
        "Coal" => Coal,
        "Magnetite" => Magnetite,
        "Water" => Water,
        "Plastic" => Plastic,
        "Glass" => Glass,
        _ => Steel
    };
}

public static class Plant3DSceneData
{
    public static readonly IReadOnlyList<Plant3DNode> MachineNodes = new Plant3DNode[]
    {
        // Stage 1 — ROM Reception (Elevated ROM Hill Bench +4.35 m)
        new(
            Id: "SP-001",
            GaLabel: "ROM Raw Coal Stockpile",
            StageLabel: "Stage 1 — ROM Reception",
            EquipmentType: "Stockpile",
            GlbFileName: "stockpile_cone.glb",
            Position: new(-13.8f, 4.35f, 16.4f),
            YawDegrees: 0f,
            Scale: new(1.1f, 0.9f, 1.1f),
            HalfExtents: new(2.1f, 1.35f, 2.1f),
            LabelHeight: 2.9f,
            DefaultColor: Color.Parse("#1C1E22"),
            DimensionsSpec: "Ø 16 m × 5.2 m cone on +4.35 m ROM hill",
            MechanicalPrinciple: "Bulk run-of-mine coal stockpile on elevated ROM hill bench reclaimed via front-end wheel loader bucket cycles.",
            ComponentDescription: "Unscreened ROM raw coal reserve (0–300 mm) feeding Stage 1 elevated primary hopper loading.",
            UpstreamIds: Array.Empty<string>(),
            DownstreamIds: new[] { "FEL-101" },
            LinkedScadaId: "AF-001"),

        new(
            Id: "FEL-101",
            GaLabel: "Front-End Wheel Loader",
            StageLabel: "Stage 1 — ROM Reception",
            EquipmentType: "Mobile Wheel Loader",
            GlbFileName: "wheel_loader_chassis.glb",
            Position: new(-13.8f, 4.35f, 13.4f),
            YawDegrees: 90f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.1f, 1.1f, 1.4f),
            LabelHeight: 2.6f,
            DefaultColor: Color.Parse("#EAB308"),
            DimensionsSpec: "3.4 m × 1.9 m × 2.4 m · 4.5 m³ rock bucket",
            MechanicalPrinciple: "4-stage shuttle cycle: scoop ROM off +4.35 m hill bench at SP-001, climb BN-101 ramp crest (+5.23 m), lift boom (38°), dump (-58°), reverse.",
            ComponentDescription: "Articulated diesel wheel loader operating atop the ROM hill plateau with hydraulic lift arms and high-dump coal bucket.",
            UpstreamIds: new[] { "SP-001" },
            DownstreamIds: new[] { "BN-101" },
            LinkedScadaId: "AF-001"),

        new(
            Id: "BN-101",
            GaLabel: "Feeder Bin / Receiving Hopper",
            StageLabel: "Stage 1 — ROM Reception",
            EquipmentType: "Receiving Hopper",
            GlbFileName: "feeder_bin_bn101.glb",
            Position: new(-13.8f, 4.35f, 8.8f),
            YawDegrees: 90f, // Right-handed CreateRotationY(+90°) maps -X ramp South (+Z) toward FEL-101
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.7f, 1.55f, 1.9f),
            LabelHeight: 3.35f,
            DefaultColor: Color.Parse("#B45309"),
            DimensionsSpec: "4.0 m × 3.6 m × 3.2 m · +4.35 m bench · 45 t surge",
            MechanicalPrinciple: "Elevated gravity surge hopper on ROM hill retaining bench with inclined loader ramp and discharge throat onto grizzly feeder.",
            ComponentDescription: "Heavy-plate ROM receiving bin mounted at +4.35 m elevation with ramp (-X rotated South toward FEL-101) and surge level monitoring.",
            UpstreamIds: new[] { "FEL-101" },
            DownstreamIds: new[] { "VF-101" },
            LinkedScadaId: "AF-001",
            HasFillLevel: true,
            FillRadius: 0.95f,
            FillBaseY: 1.45f,
            FillMaxHeight: 1.35f),

        // Stage 2 — Initial Separation (Elevated Structural Feeder Tower +4.55 m)
        new(
            Id: "VF-101",
            GaLabel: "Primary Vibro Grizzly Feeder",
            StageLabel: "Stage 2 — Initial Separation",
            EquipmentType: "Vibrating Grizzly Feeder",
            GlbFileName: "vibro_feeder_vf101.glb",
            Position: new(-13.8f, 4.55f, 6.2f),
            YawDegrees: 90f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.1f, 0.95f, 1.4f),
            LabelHeight: 2.35f,
            DefaultColor: Color.Parse("#0284C7"),
            DimensionsSpec: "3.2 m × 1.8 m × 1.7 m · +4.55 m steel tower · 75 mm",
            MechanicalPrinciple: "Elevated dual-eccentric grizzly feeder scalping <75 mm fines downhill onto CV-102 while feeding +75 mm oversize downhill onto CV-101.",
            ComponentDescription: "Heavy-duty tapered-bar grizzly feeder supported at +4.55 m on steel columns, splitting ROM coal into downhill CV-101 and CV-102.",
            UpstreamIds: new[] { "BN-101" },
            DownstreamIds: new[] { "CV-101", "CV-102" },
            LinkedScadaId: "AF-001",
            Vibrates: true),

        // Stage 3 — Jaw Crushing
        new(
            Id: "JC-101",
            GaLabel: "Primary Jaw Crusher",
            StageLabel: "Stage 3 — Jaw Crushing",
            EquipmentType: "Jaw Crusher",
            GlbFileName: "jaw_crusher_jc101.glb",
            Position: new(-13.8f, 0f, -0.6f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.4f, 1.55f, 1.3f),
            LabelHeight: 3.35f,
            DefaultColor: Color.Parse("#334155"),
            DimensionsSpec: "3.1 m × 2.6 m × 3.0 m · CSS 110 mm · 315 kW",
            MechanicalPrinciple: "Eccentric shaft and twin flywheels drive a reciprocating corrugated swing jaw against a fixed manganese jaw plate.",
            ComponentDescription: "Single-toggle primary jaw crusher reducing +75 mm ROM lumps to <110 mm before East discharge onto CV-103.",
            UpstreamIds: new[] { "CV-101" },
            DownstreamIds: new[] { "CV-103" },
            LinkedScadaId: "CR-101"),

        // Stage 4 — Double Roll Crushing
        new(
            Id: "DRC-101",
            GaLabel: "Double Roller Crusher & Surge Hopper",
            StageLabel: "Stage 4 — Double Roll Crushing",
            EquipmentType: "Double Roll Crusher",
            GlbFileName: "double_roll_crusher_drc101.glb",
            Position: new(-7.2f, 0f, -0.6f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.3f, 1.65f, 1.2f),
            LabelHeight: 3.55f,
            DefaultColor: Color.Parse("#475569"),
            DimensionsSpec: "2.8 m × 2.4 m × 3.2 m · Ø 760 mm toothed rolls",
            MechanicalPrinciple: "Two counter-rotating toothed steel rolls nip and shear recirculated +50 mm oversize lumps with minimal fines generation.",
            ComponentDescription: "Secondary toothed double-roll crusher with overhead feed surge hopper receiving CV-106 oversize and CV-103 primary crushed feed.",
            UpstreamIds: new[] { "CV-106", "CV-103" },
            DownstreamIds: new[] { "CV-104" },
            LinkedScadaId: "CR-101"),

        // Stage 6 — Central Sizing Screen
        new(
            Id: "SS-101",
            GaLabel: "Central Multi-Deck Sizing Screen",
            StageLabel: "Stage 6 — Central Sizing Screen",
            EquipmentType: "Multi-Deck Vibrating Screen",
            GlbFileName: "central_sizing_screen_ss101.glb",
            Position: new(0.2f, 0f, 0.5f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(2.1f, 1.85f, 1.45f),
            LabelHeight: 4.15f,
            DefaultColor: Color.Parse("#0284C7"),
            DimensionsSpec: "4.4 m × 2.8 m × 3.7 m · 50 mm / 6 mm dual decks",
            MechanicalPrinciple: "Dual-shaft circular-motion screen classifies converged streams (CV-102 + CV-104) into +50 mm oversize (CV-106), 6–50 mm plant feed (CV-201), and <6 mm fines (CV-105).",
            ComponentDescription: "Central sizing tower with twin overhead feed box, top-deck curved oversize chute, middlings chute, and under-screen fines hopper.",
            UpstreamIds: new[] { "CV-102", "CV-104" },
            DownstreamIds: new[] { "CV-106", "CV-201", "CV-105" },
            LinkedScadaId: "SC-102",
            Vibrates: true),

        // Wash Plant Feed & Surge
        new(
            Id: "BN-201",
            GaLabel: "Plant Feed Surge Bin",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Surge Bin",
            GlbFileName: "surge_bin_bn201.glb",
            Position: new(11.8f, 0f, -0.8f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.45f, 1.95f, 1.45f),
            LabelHeight: 4.15f,
            DefaultColor: Color.Parse("#0EA5E9"),
            DimensionsSpec: "3.0 m × 3.0 m × 3.9 m · 60 t live surge",
            MechanicalPrinciple: "Elevated conical-bottom surge bin buffering dry plant feed fluctuations before steady-rate wet wash plant feeding.",
            ComponentDescription: "Structural steel-legged surge bin receiving sized 6–50 mm feed from CV-201 and discharging onto CV-202.",
            UpstreamIds: new[] { "CV-201" },
            DownstreamIds: new[] { "CV-202" },
            LinkedScadaId: "SL-601",
            HasFillLevel: true,
            FillRadius: 1.05f,
            FillBaseY: 1.65f,
            FillMaxHeight: 1.65f),

        new(
            Id: "DS-201",
            GaLabel: "Desliming Screen & Vibro Feeder",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Desliming Screen",
            GlbFileName: "desliming_screen_ds201.glb",
            Position: new(8.8f, 0f, -9.8f),
            YawDegrees: 180f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.7f, 1.45f, 1.25f),
            LabelHeight: 3.15f,
            DefaultColor: Color.Parse("#06B6D4"),
            DimensionsSpec: "3.5 m × 2.4 m × 2.8 m · 0.5 mm wedge-wire",
            MechanicalPrinciple: "High-pressure rinse sprays and high-G vibration strip <0.5 mm slimes so magnetite dense medium is not contaminated by clays.",
            ComponentDescription: "Wet desliming screen & feed chute receiving CV-202 and discharging washed +0.5 mm coal West onto CV-203.",
            UpstreamIds: new[] { "CV-202" },
            DownstreamIds: new[] { "CV-203" },
            LinkedScadaId: "SC-102",
            Vibrates: true),

        new(
            Id: "WP-301",
            GaLabel: "Dense-Medium Wash Plant (Mixing Box, Top Cyclone, Dual Wash Screens & Drip Bucket)",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Dense-Medium Cyclone Module",
            GlbFileName: "wash_plant_module_wp301.glb",
            Position: new(-0.8f, 0f, -7.8f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(3.6f, 2.75f, 2.6f),
            LabelHeight: 5.75f,
            DefaultColor: Color.Parse("#1D4ED8"),
            DimensionsSpec: "8.8 m × 5.2 m × 5.5 m open-top frame (no roof) · full-length 4.3 m diamond DMC · SG 1.52",
            MechanicalPrinciple: "CV-203 dumps deslimed coal into the massive East Mixing Box (water + TK-301 magnetite); PIPE-304 sweeps around the North/West conveyors up to the full-length asymmetric diamond DMC-301, whose vortex finder and apex return clean coal and discard to the dual 4.3 m drain & rinse screens.",
            ComponentDescription: "Open-top dense-medium wash plant: ground-floor under-screen tanks & magnetite drip bucket/reclaim pump, East mixing box & sweeping PIPE-304 manifold, dual 4.3 m product/discard drain & rinse screens with water sprayers, and full-length top-mounted diamond cyclone.",
            UpstreamIds: new[] { "CV-203", "TK-301" },
            DownstreamIds: new[] { "CV-301", "CV-302", "MS-301" },
            LinkedScadaId: "DM-301"),

        new(
            Id: "MS-301",
            GaLabel: "Wet-Drum Magnetic Separator (Macsep)",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Magnetic Separator",
            GlbFileName: "magnetic_separator_ms301.glb",
            Position: new(-1.6f, 0f, -11.4f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.1f, 1.0f, 0.9f),
            LabelHeight: 2.55f,
            DefaultColor: Color.Parse("#0D9488"),
            DimensionsSpec: "2.2 m × 1.8 m × 2.0 m · 96.4% recovery",
            MechanicalPrinciple: "Rotating permanent-magnet drum captures ferromagnetic magnetite particles from dilute rinse water while non-magnetic slimes report to thickener.",
            ComponentDescription: "Concurrent wet-drum magnetic separator recovering dense-medium magnetite for return to TK-301.",
            UpstreamIds: new[] { "WP-301" },
            DownstreamIds: new[] { "TK-301" },
            LinkedScadaId: "MS-301",
            HasRotatingTop: true,
            RotatingTopY: 1.38f,
            RotatingTopLength: 1.45f),

        new(
            Id: "TK-301",
            GaLabel: "Magnetite Medium Tank",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Medium Sump Tank",
            GlbFileName: "magnetite_tank_tk301.glb",
            Position: new(-5.4f, 0f, -12.0f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.45f, 1.7f, 1.45f),
            LabelHeight: 3.75f,
            DefaultColor: Color.Parse("#B45309"),
            DimensionsSpec: "Ø 2.9 m × 3.4 m · SG 1.52 controlled",
            MechanicalPrinciple: "Agitated holding sump blending recovered magnetite with clarified dilution water to maintain target cyclone medium specific gravity.",
            ComponentDescription: "Correct-medium sump tank feeding WP-301 mixing box and receiving magnetite concentrate from MS-301.",
            UpstreamIds: new[] { "MS-301", "PP-201" },
            DownstreamIds: new[] { "WP-301" },
            LinkedScadaId: "TK-301",
            HasFillLevel: true,
            FillRadius: 1.24f,
            FillBaseY: 0.22f,
            FillMaxHeight: 2.05f),

        new(
            Id: "TK-201",
            GaLabel: "High-Rate Thickener",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "High-Rate Thickener",
            GlbFileName: "thickener_tk201.glb",
            Position: new(-1.2f, 0f, -15.8f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(2.85f, 1.3f, 2.85f),
            LabelHeight: 2.95f,
            DefaultColor: Color.Parse("#1E40AF"),
            DimensionsSpec: "Ø 5.7 m × 2.6 m · flocculant-assisted",
            MechanicalPrinciple: "Rotating central rake mechanism consolidates flocculated fine tailings to the underflow cone while clarified water overflows the peripheral launder.",
            ComponentDescription: "High-rate clarifier/thickener closing the plant process water loop (87.4% water recovery).",
            UpstreamIds: new[] { "WP-301", "PP-203" },
            DownstreamIds: new[] { "PP-201", "PP-202" },
            LinkedScadaId: "TK-201",
            HasFillLevel: true,
            FillRadius: 2.52f,
            FillBaseY: 0.25f,
            FillMaxHeight: 1.30f,
            HasRotatingTop: true,
            RotatingTopY: 1.92f,
            RotatingTopLength: 5.4f),

        new(
            Id: "PP-201",
            GaLabel: "Underflow Pump A (Duty)",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Centrifugal Slurry Pump",
            GlbFileName: "centrifugal_pump.glb",
            Position: new(-4.6f, 0f, -15.0f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(0.8f, 0.7f, 0.6f),
            LabelHeight: 1.75f,
            DefaultColor: Color.Parse("#0EA5E9"),
            DimensionsSpec: "1.6 m × 1.2 m × 1.4 m · 385 m³/h @ 4.2 bar",
            MechanicalPrinciple: "Rubber-lined centrifugal impeller pumps thickened underflow slurry from TK-201 cone.",
            ComponentDescription: "Primary duty thickener underflow pump with VFD speed control.",
            UpstreamIds: new[] { "TK-201" },
            DownstreamIds: new[] { "TK-301" },
            LinkedScadaId: "PP-201"),

        new(
            Id: "PP-202",
            GaLabel: "Underflow Pump B (Standby)",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Centrifugal Slurry Pump",
            GlbFileName: "centrifugal_pump.glb",
            Position: new(-4.6f, 0f, -16.3f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(0.8f, 0.7f, 0.6f),
            LabelHeight: 1.75f,
            DefaultColor: Color.Parse("#475569"),
            DimensionsSpec: "1.6 m × 1.2 m × 1.4 m · Standby unit",
            MechanicalPrinciple: "Hot-standby centrifugal pump manifolded in parallel with PP-201 for zero-downtime changeover.",
            ComponentDescription: "Redundant thickener underflow pump (typically Stopped in standby).",
            UpstreamIds: new[] { "TK-201" },
            DownstreamIds: new[] { "TK-301" },
            LinkedScadaId: "PP-202"),

        new(
            Id: "PP-203",
            GaLabel: "Flocculant & Clarified Water Pump",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Dosing / Return Pump",
            GlbFileName: "centrifugal_pump.glb",
            Position: new(2.2f, 0f, -15.0f),
            YawDegrees: 180f,
            Scale: new(0.95f, 0.95f, 0.95f),
            HalfExtents: new(0.75f, 0.7f, 0.6f),
            LabelHeight: 1.70f,
            DefaultColor: Color.Parse("#10B981"),
            DimensionsSpec: "1.5 m × 1.1 m × 1.3 m · 24.5 L/min dosing",
            MechanicalPrinciple: "Metered flocculant & clarified water return pump supporting rapid settling in TK-201 and rinse sprays at WP-301.",
            ComponentDescription: "Thickener flocculant dosing and clarified process water booster pump.",
            UpstreamIds: new[] { "TK-201" },
            DownstreamIds: new[] { "WP-301" },
            LinkedScadaId: "PP-203"),

        // Product & Discard Stockpiles
        new(
            Id: "SP-301",
            GaLabel: "Clean Product Coal Stockpile",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Product Stockpile",
            GlbFileName: "stockpile_cone.glb",
            Position: new(-9.8f, 0f, -5.2f),
            YawDegrees: 0f,
            Scale: new(1.05f, 0.88f, 1.05f),
            HalfExtents: new(1.9f, 1.3f, 1.9f),
            LabelHeight: 2.8f,
            DefaultColor: Color.Parse("#0C0F14"),
            DimensionsSpec: "Ø 14 m × 4.6 m · Low-ash product",
            MechanicalPrinciple: "Conical radial discharge pile built by Product Conv CV-301 below dewatering screen.",
            ComponentDescription: "Washed clean coal product stockpile (9.4% ash, 27.8 MJ/kg) ready for dispatch.",
            UpstreamIds: new[] { "CV-301" },
            DownstreamIds: Array.Empty<string>(),
            LinkedScadaId: "SL-601"),

        new(
            Id: "SP-302",
            GaLabel: "Discard / Refuse Stockpile",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Discard Stockpile",
            GlbFileName: "stockpile_cone.glb",
            Position: new(-10.6f, 0f, -8.2f),
            YawDegrees: 0f,
            Scale: new(0.95f, 0.78f, 0.95f),
            HalfExtents: new(1.7f, 1.15f, 1.7f),
            LabelHeight: 2.55f,
            DefaultColor: Color.Parse("#22262D"),
            DimensionsSpec: "Ø 12 m × 4.0 m · High-ash shale discard",
            MechanicalPrinciple: "Conical refuse pile fed by Discard Conv CV-302 from cyclone sinks dewatering.",
            ComponentDescription: "High-density rock/shale reject stockpile separated by dense-medium cyclone.",
            UpstreamIds: new[] { "CV-302" },
            DownstreamIds: Array.Empty<string>(),
            LinkedScadaId: "CV-501")
    };

    public static readonly IReadOnlyList<ConveyorSpec> Conveyors = new ConveyorSpec[]
    {
        new(
            Id: "CV-101",
            GaLabel: "Jaw feed conv",
            StageLabel: "Stage 2 — Initial Separation",
            TailPos: new(-13.8f, 4.75f, 5.4f),
            HeadPos: new(-13.8f, 3.35f, 0.8f),
            Width: 0.95f,
            HeadDropTargetY: 2.15f,
            MaterialClass: CoalMaterialClass.RawRom,
            LinkedScadaId: "CV-101",
            UpstreamIds: new[] { "VF-101" },
            DownstreamIds: new[] { "JC-101" },
            DimensionsSpec: "4.8 m declined (-17°) · 1200 mm belt · 35° trough",
            MechanicalPrinciple: "Declined troughed belt conveying +75 mm grizzly oversize downhill from elevated VF-101 into Primary Jaw Crusher JC-101 feed throat.",
            ComponentDescription: "Stage 2 Path A downhill conveyor with tail skirtboard, safety walkway, and enclosed head chute into JC-101.",
            HeadDischargeOffset: new(0f, 0f, -0.95f)),

        new(
            Id: "CV-102",
            GaLabel: "Under size conv",
            StageLabel: "Stage 2 — Initial Separation",
            TailPos: new(-12.8f, 4.55f, 6.1f),
            HeadPos: new(-1.9f, 3.85f, 1.15f),
            Width: 0.85f,
            HeadDropTargetY: 2.95f,
            MaterialClass: CoalMaterialClass.ScalpedUndersize,
            LinkedScadaId: "CV-102",
            UpstreamIds: new[] { "VF-101" },
            DownstreamIds: new[] { "SS-101" },
            DimensionsSpec: "12.0 m diagonal NE declined (-3.4°) · 900 mm belt",
            MechanicalPrinciple: "Scalped <75 mm grizzly undersize bypasses crushing and conveys downhill NE from elevated VF-101 into Central Sizing Screen SS-101 dual feed box (Stream #1).",
            ComponentDescription: "Stage 2 Path B downhill bypass conveyor with structural support bents and maintenance walkway.",
            HeadDischargeOffset: new(0.65f, 0f, -0.25f)),

        new(
            Id: "CV-103",
            GaLabel: "Sizing screen feed",
            StageLabel: "Stage 3 — Jaw Crushing",
            TailPos: new(-12.5f, 0.75f, -0.6f),
            HeadPos: new(-8.3f, 2.75f, -0.6f),
            Width: 0.95f,
            HeadDropTargetY: 1.35f,
            MaterialClass: CoalMaterialClass.CrushedPrimary,
            LinkedScadaId: "CV-101",
            UpstreamIds: new[] { "JC-101" },
            DownstreamIds: new[] { "DRC-101" },
            DimensionsSpec: "4.6 m East · 1050 mm belt · 24° incline",
            MechanicalPrinciple: "Collects <110 mm primary crushed discharge under JC-101 and transfers East into DRC-101 bypass/transfer chute onto CV-104.",
            ComponentDescription: "Jaw crusher discharge conveyor linking Stage 3 to the Stage 4–5 transfer tower.",
            HeadDischargeOffset: new(0.85f, 0f, 0.10f)),

        new(
            Id: "CV-106",
            GaLabel: "Oversize conv",
            StageLabel: "Stage 4 — Double Roll Crushing",
            TailPos: new(1.8f, 2.55f, -1.8f),
            HeadPos: new(-7.2f, 4.35f, -1.8f),
            Width: 0.82f,
            HeadDropTargetY: 2.85f,
            MaterialClass: CoalMaterialClass.ScreenOversize,
            LinkedScadaId: "CV-102",
            UpstreamIds: new[] { "SS-101" },
            DownstreamIds: new[] { "DRC-101" },
            DimensionsSpec: "9.2 m West return · 900 mm belt · closed circuit",
            MechanicalPrinciple: "Closed-circuit recirculation conveyor returning +50 mm top-deck oversize from SS-101 curved chute West into DRC-101 surge hopper.",
            ComponentDescription: "Elevated return belt with curved SS-101 oversize entry chute and overhead discharge into Double Roller Crusher.",
            FeedChuteWaypoints: new[]
            {
                new Vector3(2.2f, 3.15f, 0.3f),
                new Vector3(2.4f, 2.85f, -1.8f),
                new Vector3(1.8f, 2.55f, -1.8f)
            },
            HeadDischargeOffset: new(0f, 0f, 0.85f)),

        new(
            Id: "CV-104",
            GaLabel: "Sizing screen feed conv",
            StageLabel: "Stage 5 — Secondary Transfer",
            TailPos: new(-6.3f, 0.80f, -0.4f),
            HeadPos: new(-1.9f, 4.20f, 0.15f),
            Width: 0.95f,
            HeadDropTargetY: 2.95f,
            MaterialClass: CoalMaterialClass.CrushedRoll,
            LinkedScadaId: "CV-101",
            UpstreamIds: new[] { "DRC-101" },
            DownstreamIds: new[] { "SS-101" },
            DimensionsSpec: "5.6 m East incline · 1050 mm belt",
            MechanicalPrinciple: "Elevates combined DRC-101 roll-crushed product and CV-103 primary stream into SS-101 dual feed box alongside CV-102 (Stream #2).",
            ComponentDescription: "Stage 5 convergence conveyor delivering crushed coal to Central Sizing Screen SS-101.",
            HeadDischargeOffset: new(0.65f, 0f, 0.15f)),

        new(
            Id: "CV-105",
            GaLabel: "Fines conv under the sizing screen",
            StageLabel: "Stage 6 — Central Sizing Screen",
            TailPos: new(-1.2f, 0.55f, 0.5f),
            HeadPos: new(2.8f, 1.15f, 2.2f),
            Width: 0.78f,
            HeadDropTargetY: 0.25f,
            MaterialClass: CoalMaterialClass.SizedFines,
            LinkedScadaId: "CV-102",
            UpstreamIds: new[] { "SS-101" },
            DownstreamIds: Array.Empty<string>(),
            DimensionsSpec: "4.4 m SE takeoff · 900 mm belt",
            MechanicalPrinciple: "Extracts <6 mm dry fines from SS-101 under-pan hopper away from the coarse dense-medium wash plant circuit.",
            ComponentDescription: "Under-screen fines takeaway conveyor routing <6 mm dry coal to fines handling.",
            HeadDischargeOffset: new(0.25f, 0f, 0.10f)),

        new(
            Id: "CV-201",
            GaLabel: "Plant feed conv",
            StageLabel: "Stage 6 — Central Sizing Screen",
            TailPos: new(2.3f, 1.45f, 0.4f),
            HeadPos: new(10.6f, 4.25f, -0.8f),
            Width: 0.92f,
            HeadDropTargetY: 3.10f,
            MaterialClass: CoalMaterialClass.PlantFeed,
            LinkedScadaId: "CV-101",
            UpstreamIds: new[] { "SS-101" },
            DownstreamIds: new[] { "BN-201" },
            DimensionsSpec: "8.8 m East · 1050 mm belt · 19° incline",
            MechanicalPrinciple: "Carries sized 6–50 mm middlings from SS-101 East into Plant Feed Surge Bin BN-201.",
            ComponentDescription: "Main plant feed transfer conveyor bridging dry screening (Stage 6) and the wet washing plant.",
            HeadDischargeOffset: new(0.95f, 0f, 0f)),

        new(
            Id: "CV-202",
            GaLabel: "Desliming screen feed",
            StageLabel: "Wash Plant & Medium Recovery",
            TailPos: new(11.4f, 0.85f, -1.9f),
            HeadPos: new(9.6f, 3.95f, -8.6f),
            Width: 0.90f,
            HeadDropTargetY: 2.55f,
            MaterialClass: CoalMaterialClass.PlantFeed,
            LinkedScadaId: "CV-101",
            UpstreamIds: new[] { "BN-201" },
            DownstreamIds: new[] { "DS-201" },
            DimensionsSpec: "7.6 m NNW · 1050 mm belt · 24° incline",
            MechanicalPrinciple: "Draws metered feed from BN-201 discharge gate North-Northwest to Desliming Screen DS-201.",
            ComponentDescription: "Inclined surge-bin reclaim conveyor feeding the wet desliming station.",
            HeadDischargeOffset: new(-0.45f, 0f, -0.85f)),

        new(
            Id: "CV-203",
            GaLabel: "Mixing box feed conv",
            StageLabel: "Wash Plant & Medium Recovery",
            TailPos: new(7.0f, 2.15f, -9.5f),
            HeadPos: new(2.9f, 1.95f, -8.3f),
            Width: 0.90f,
            HeadDropTargetY: 1.50f,
            MaterialClass: CoalMaterialClass.DeslimedCoal,
            LinkedScadaId: "DM-301",
            UpstreamIds: new[] { "DS-201" },
            DownstreamIds: new[] { "WP-301" },
            DimensionsSpec: "4.3 m WSW · 1200 mm belt · 3° decline into Mixing Box",
            MechanicalPrinciple: "Dumps deslimed +0.5 mm coal from DS-201 directly into the open top of the WP-301 Massive Mixing Box.",
            ComponentDescription: "Mixing box feed conveyor linking desliming screen DS-201 to the East massive mixing box.",
            HeadDischargeOffset: new(-0.45f, 0f, 0f)),

        new(
            Id: "CV-301",
            GaLabel: "Product conv",
            StageLabel: "Wash Plant & Medium Recovery",
            TailPos: new(-4.2f, 2.10f, -6.8f),
            HeadPos: new(-8.8f, 2.65f, -5.4f),
            Width: 0.85f,
            HeadDropTargetY: 1.55f,
            MaterialClass: CoalMaterialClass.CleanProduct,
            LinkedScadaId: "CV-102",
            UpstreamIds: new[] { "WP-301" },
            DownstreamIds: new[] { "SP-301" },
            DimensionsSpec: "4.8 m WSW · 900 mm belt",
            MechanicalPrinciple: "Conveys dewatered low-ash clean coal product from WP-301 onto Product Stockpile SP-301.",
            ComponentDescription: "Clean coal product stacker conveyor.",
            HeadDischargeOffset: new(-0.75f, 0f, 0.15f)),

        new(
            Id: "CV-302",
            GaLabel: "discard conv",
            StageLabel: "Wash Plant & Medium Recovery",
            TailPos: new(-4.4f, 2.05f, -8.4f),
            HeadPos: new(-9.6f, 2.55f, -8.2f),
            Width: 0.80f,
            HeadDropTargetY: 1.45f,
            MaterialClass: CoalMaterialClass.RefuseDiscard,
            LinkedScadaId: "CV-501",
            UpstreamIds: new[] { "WP-301" },
            DownstreamIds: new[] { "SP-302" },
            DimensionsSpec: "5.2 m WSW · 900 mm belt",
            MechanicalPrinciple: "Conveys high-ash shale sinks from WP-301 refuse screen onto Discard Stockpile SP-302.",
            ComponentDescription: "Plant reject / shale discard stacker conveyor.",
            HeadDischargeOffset: new(-0.75f, 0f, 0f))
    };

    public static readonly IReadOnlyList<Plant3DNode> ConveyorNodes = BuildConveyorNodes();
    public static readonly IReadOnlyList<Plant3DNode> AllNodes = MachineNodes.Concat(ConveyorNodes).ToArray();

    /// <summary>Returns the complete set of 29 CHPP GA plant nodes (18 machines + 11 conveyors).</summary>
    public static IReadOnlyList<Plant3DNode> BuildNodes() => AllNodes;

    /// <summary>Backward-compatible alias for machine placements.</summary>
    public static IReadOnlyList<Plant3DNode> Placements => MachineNodes;

    private static IReadOnlyList<Plant3DNode> BuildConveyorNodes()
    {
        var list = new List<Plant3DNode>(Conveyors.Count);
        foreach (var c in Conveyors)
        {
            var mid = (c.TailPos + c.HeadPos) * 0.5f;
            var diff = c.HeadPos - c.TailPos;
            float yawRad = MathF.Atan2(-diff.Z, diff.X);
            float maxY = MathF.Max(c.TailPos.Y, c.HeadPos.Y);
            var minV = Vector3.Min(c.TailPos, c.HeadPos);
            var maxV = Vector3.Max(c.TailPos, c.HeadPos);
            var half = new Vector3(
                MathF.Max(0.6f, (maxV.X - minV.X) * 0.5f + c.Width * 0.5f),
                MathF.Max(0.55f, (maxV.Y - minV.Y) * 0.5f + 0.55f),
                MathF.Max(0.6f, (maxV.Z - minV.Z) * 0.5f + c.Width * 0.5f));

            list.Add(new Plant3DNode(
                Id: c.Id,
                GaLabel: c.GaLabel,
                StageLabel: c.StageLabel,
                EquipmentType: $"Belt Conveyor ({c.MaterialClass})",
                GlbFileName: "conveyor_segment.glb",
                Position: mid,
                YawDegrees: yawRad * (180f / MathF.PI),
                Scale: Vector3.One,
                HalfExtents: half,
                LabelHeight: (maxY - mid.Y) + 1.05f,
                DefaultColor: GetMaterialColor(c.MaterialClass),
                DimensionsSpec: c.DimensionsSpec,
                MechanicalPrinciple: c.MechanicalPrinciple,
                ComponentDescription: c.ComponentDescription,
                UpstreamIds: c.UpstreamIds,
                DownstreamIds: c.DownstreamIds,
                LinkedScadaId: c.LinkedScadaId,
                IsConveyor: true));
        }
        return list;
    }

    public static Plant3DNode? FindNode(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var n in InteractiveNodes)
        {
            if (string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase))
                return n;
        }
        return null;
    }

    public static ConveyorSpec? FindConveyor(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var c in Conveyors)
        {
            if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    public static readonly IReadOnlyList<FoundationPad> FoundationPads = new FoundationPad[]
    {
        new("STAGE 1–2: ROM & PRIMARY FEED",    new(-13.8f, -0.06f, 11.4f),  new(6.0f, 0.12f, 15.8f), Color.Parse("#F59E0B")),
        new("STAGE 3–5: PRIMARY & ROLL CRUSH",  new(-10.5f, -0.06f, -0.8f),  new(10.2f, 0.12f, 4.4f), Color.Parse("#FB923C")),
        new("STAGE 6: CENTRAL SIZING SCREEN",   new(0.4f, -0.06f, 0.6f),     new(5.6f, 0.12f, 4.8f),  Color.Parse("#38BDF8")),
        new("SURGE & DESLIMING FEED",           new(10.4f, -0.06f, -5.2f),   new(6.2f, 0.12f, 12.0f), Color.Parse("#0EA5E9")),
        new("WASH PLANT & PRODUCT STOCKPILES",  new(-5.0f, -0.06f, -7.2f),   new(14.5f, 0.12f, 6.2f), Color.Parse("#22C55E")),
        new("MEDIUM RECOVERY & THICKENER",      new(-1.6f, -0.06f, -13.9f),  new(11.6f, 0.12f, 7.6f), Color.Parse("#14B8A6"))
    };

    /// <summary>
    /// Orthogonal 3D slurry, magnetite dense-medium, and clarified water piping routes
    /// connecting Wash Plant WP-301, Macsep MS-301, Magnetite Tank TK-301, and Thickener TK-201.
    /// </summary>
    public static readonly IReadOnlyList<PipeRoute> PipeRoutes = new PipeRoute[]
    {
        new("MS-301", FlowMedium.Magnetite, 0.075f, new Vector3[]
        {
            new(-1.9f, 0.55f, -8.6f),
            new(-1.9f, 1.35f, -9.2f),
            new(-1.6f, 1.35f, -11.4f)
        },
            Id: "PIPE-301",
            GaLabel: "Drip Bucket to Macsep Magnetite Reclaim Pipe",
            DimensionsSpec: "DN150 rubber-lined steel · 3.0 m run · 1.8 m/s",
            MechanicalPrinciple: "PP-302 pumps dilute magnetite rinse collected in the BK-301 drip bucket to the MS-301 wet-drum magnetic separator.",
            ComponentDescription: "Magnetite reclaim line from the ground-floor drip bucket to the Macsep.",
            UpstreamIds: new[] { "PP-302" },
            DownstreamIds: new[] { "MS-301" }),
        new("MS-301", FlowMedium.Magnetite, 0.07f, new Vector3[]
        {
            new(-2.3f, 1.10f, -11.4f),
            new(-5.4f, 1.10f, -11.4f),
            new(-5.4f, 1.10f, -12.0f)
        },
            Id: "PIPE-302",
            GaLabel: "Macsep to Magnetite Tank Concentrate Return Pipe",
            DimensionsSpec: "DN125 rubber-lined steel · 3.7 m run",
            MechanicalPrinciple: "Gravity/pumped return of recovered magnetite concentrate from MS-301 into the TK-301 correct-medium sump.",
            ComponentDescription: "Recovered magnetite concentrate return line.",
            UpstreamIds: new[] { "MS-301" },
            DownstreamIds: new[] { "TK-301" }),
        new("DM-301", FlowMedium.Magnetite, 0.08f, new Vector3[]
        {
            new(-5.4f, 1.45f, -12.0f),
            new(-5.4f, 1.45f, -10.2f),
            new(2.30f, 1.45f, -10.2f),
            new(2.30f, 1.45f, -8.30f)
        },
            Id: "PIPE-303",
            GaLabel: "Magnetite Tank to Mixing Box Dense-Medium Feed Pipe",
            DimensionsSpec: "DN150 rubber-lined steel · 11.4 m run · SG 1.52 medium",
            MechanicalPrinciple: "Delivers correct dense medium (magnetite + water, SG 1.52) from TK-301 into the MX-301 mixing box.",
            ComponentDescription: "Dense-medium feed line into the massive mixing box.",
            UpstreamIds: new[] { "TK-301" },
            DownstreamIds: new[] { "MX-301" }),
        new("DM-301", FlowMedium.Coal, 0.095f, new Vector3[]
        {
            new(2.30f, 0.55f, -7.05f),
            new(2.30f, 5.50f, -7.05f),
            new(-1.00f, 5.50f, -9.00f),
            new(-4.00f, 4.68f, -8.05f)
        },
            Id: "PIPE-304",
            GaLabel: "Mixing Box to West Cyclone Inlet Sweeping Feed Pipe (Coal-Magnetite Mix)",
            DimensionsSpec: "DN250 ceramic-lined manifold · 14.2 m sweeping run · 4.35 m lift",
            MechanicalPrinciple: "PP-301 pumps coal-magnetite slurry from MX-301, sweeping North/West around the conveyors and entering the West side of DMC-301 past PT-301.",
            ComponentDescription: "Sweeping cyclone feed pipe from the mixing box around the conveyor side into the West DMC-301 inlet.",
            UpstreamIds: new[] { "PP-301" },
            DownstreamIds: new[] { "DMC-301" }),
        new("WP-301", FlowMedium.Slurry, 0.08f, new Vector3[]
        {
            new(-0.8f, 0.90f, -9.4f),
            new(-0.8f, 0.90f, -13.5f),
            new(-1.2f, 1.85f, -15.8f)
        },
            Id: "PIPE-305",
            GaLabel: "Wash Plant Slimes Effluent to Thickener Feedwell Pipe",
            DimensionsSpec: "DN200 HDPE · 6.5 m run",
            MechanicalPrinciple: "Carries non-magnetic slimes effluent from the wash plant to the TK-201 thickener feedwell.",
            ComponentDescription: "Slimes effluent line to the high-rate thickener.",
            UpstreamIds: new[] { "WP-301" },
            DownstreamIds: new[] { "TK-201" }),
        new("PP-201", FlowMedium.Slurry, 0.075f, new Vector3[]
        {
            new(-1.2f, 0.45f, -15.8f),
            new(-4.6f, 0.45f, -15.8f),
            new(-4.6f, 0.45f, -15.0f),
            new(-5.4f, 0.95f, -15.0f),
            new(-5.4f, 0.95f, -12.0f)
        },
            Id: "PIPE-306",
            GaLabel: "Thickener Underflow to Duty Pump & Medium Sump Pipe",
            DimensionsSpec: "DN150 rubber-lined steel · 8.1 m run · 4.2 bar",
            MechanicalPrinciple: "Thickened underflow from the TK-201 cone is pumped by PP-201 to the TK-301 medium sump.",
            ComponentDescription: "Thickener underflow discharge line.",
            UpstreamIds: new[] { "TK-201", "PP-201" },
            DownstreamIds: new[] { "TK-301" }),
        new("PP-203", FlowMedium.Froth, 0.075f, new Vector3[]
        {
            new(1.4f, 1.45f, -15.8f),
            new(2.2f, 1.45f, -15.8f),
            new(2.2f, 0.65f, -15.0f),
            new(2.30f, 1.15f, -8.30f),
            new(2.30f, 3.10f, -8.30f),
            new(-2.2f, 3.10f, -7.60f)
        },
            Id: "PIPE-307",
            GaLabel: "Clarified Water Return Pipe to Mixing Box & Screen Sprayers",
            DimensionsSpec: "DN100 galvanised steel · 14.6 m run",
            MechanicalPrinciple: "PP-203 returns clarified process water from TK-201 to the MX-301 mixing box and the SPRAY-301 magnetite-reclaim spray bars.",
            ComponentDescription: "Clarified process water return line.",
            UpstreamIds: new[] { "PP-203" },
            DownstreamIds: new[] { "MX-301", "SPRAY-301" }),
        new("PP-203", FlowMedium.Froth, 0.065f, new Vector3[]
        {
            new(2.25f, 0.95f, -11.50f),
            new(7.45f, 0.95f, -11.50f),
            new(7.45f, 2.32f, -8.88f)
        },
            Id: "PIPE-308",
            GaLabel: "PP-203 to DS-201 Desliming Spray Water Supply Pipe",
            DimensionsSpec: "DN125 galvanised steel · 8.2 m run · 3.5 bar",
            MechanicalPrinciple: "Branches clarified process water from the PP-203 return line through VALVE-204 to the DS-201 desliming spray-bar manifold SPRAY-201.",
            ComponentDescription: "Clarified spray-water supply branch to Desliming Screen DS-201.",
            UpstreamIds: new[] { "PP-203", "PIPE-307" },
            DownstreamIds: new[] { "VALVE-204", "SPRAY-201" }),
        new("PP-203", FlowMedium.Froth, 0.045f, new Vector3[]
        {
            new(2.20f, 0.95f, -15.00f),
            new(2.20f, 2.10f, -15.80f),
            new(-0.85f, 2.10f, -15.80f)
        },
            Id: "PIPE-309",
            GaLabel: "PP-203 to TK-201 Feedwell Flocculant Dosing Line",
            DimensionsSpec: "DN80 stainless steel · 4.5 m run · 24.5 L/min",
            MechanicalPrinciple: "Delivers metered flocculant solution from PP-203 through dosing control valve VALVE-203 into the TK-201 thickener feedwell.",
            ComponentDescription: "Flocculant polymer dosing line feeding Thickener TK-201 feedwell.",
            UpstreamIds: new[] { "PP-203" },
            DownstreamIds: new[] { "VALVE-203", "TK-201" }),
        new("SC-102", FlowMedium.Slurry, 0.08f, new Vector3[]
        {
            new(8.75f, 0.35f, -9.80f),
            new(8.75f, 0.55f, -13.80f),
            new(-0.55f, 0.55f, -13.80f),
            new(-0.95f, 1.85f, -15.80f)
        },
            Id: "PIPE-310",
            GaLabel: "DS-201 Underpan Slimes Drain to TK-201 Feedwell Pipe",
            DimensionsSpec: "DN160 HDPE slurry line · 15.7 m run",
            MechanicalPrinciple: "Routes <0.5 mm clay-slimes effluent collected in DS-201 underpan PAN-201 to the TK-201 high-rate thickener feedwell.",
            ComponentDescription: "Desliming screen slimes effluent drain line to Thickener TK-201.",
            UpstreamIds: new[] { "PAN-201" },
            DownstreamIds: new[] { "TK-201" }),
        new("PP-202", FlowMedium.Slurry, 0.075f, new Vector3[]
        {
            new(-4.60f, 0.45f, -15.80f),
            new(-4.60f, 0.45f, -16.30f),
            new(-5.40f, 0.95f, -16.30f),
            new(-5.40f, 0.95f, -15.00f)
        },
            Id: "PIPE-311",
            GaLabel: "PP-202 Standby Underflow Pump Suction/Discharge Manifold",
            DimensionsSpec: "DN150 rubber-lined steel · 2.7 m standby loop",
            MechanicalPrinciple: "Parallel standby suction and discharge manifold linking TK-201 cone via PP-202 into the PIPE-306 header toward TK-301.",
            ComponentDescription: "Standby thickener underflow pump manifold for PP-202.",
            UpstreamIds: new[] { "TK-201", "PP-202" },
            DownstreamIds: new[] { "PIPE-306", "TK-301" })
    };

    /// <summary>
    /// §5 Enhanced pipe routes with beveled joint geometry and flange details at connections.
    /// </summary>
    public static readonly IReadOnlyList<EnhancedPipeRoute> EnhancedPipeRoutes = new EnhancedPipeRoute[]
    {
        new(
            SourceEquipmentId: "PP-301",
            Medium: FlowMedium.Coal,
            Radius: 0.095f,
            Waypoints: new[]
            {
                new Vector3(2.30f, 0.55f, -7.05f),
                new Vector3(2.30f, 5.50f, -7.05f),
                new Vector3(-1.00f, 5.50f, -9.00f),
                new Vector3(-4.00f, 4.68f, -8.05f)
            },
            Id: "PIPE-304",
            GaLabel: "Mixing Box to West Cyclone Inlet Sweeping Feed Pipe (Coal-Magnetite Mix)",
            StageLabel: "Wash Plant & Medium Recovery",
            DimensionsSpec: "DN250 ceramic-lined manifold · 14.2 m sweeping run · 4.35 m lift",
            MechanicalPrinciple: "PP-301 pumps coal-magnetite slurry from MX-301, sweeping North/West around the conveyors and entering the West side of DMC-301 past PT-301.",
            ComponentDescription: "Sweeping cyclone feed pipe from the mixing box around the conveyor side into the West DMC-301 inlet.",
            LODVertexCount: 6500,
            HasBeveledJoints: true,
            HasFlangeDetails: true,
            PipeMaterial: "Ceramic-Lined")
    };

    /// <summary>
    /// Individually selectable sub-equipment across Stages 1–6 and the WP-301 Wash Plant / Thickener circuit.
    /// These are not part of the 29-node primary GA topology; they are rendered inside the primary GLB assemblies.
    /// </summary>
    public static readonly IReadOnlyList<Plant3DNode> SubEquipmentNodes = new Plant3DNode[]
    {
        // Existing WP-301 sub-equipment (8)
        SubNode("MX-301", "Massive Dense-Medium Mixing Box", "Mixing Box (Agitated)", new(2.30f, 0.0f, -8.30f), new(1.12f, 1.15f, 1.12f), 2.75f,
            "#1A5C96", "2.1 m × 2.1 m × 2.15 m · agitated · open top",
            "Receives deslimed coal from CV-203, magnetite medium (PIPE-303) and process water (PIPE-307) and blends them into a homogeneous dense-medium slurry.",
            "East massive mixing box with agitator bridge, water and magnetite inlets.",
            new[] { "CV-203", "PIPE-303", "PIPE-307" }, new[] { "PP-301" }, "DM-301"),
        SubNode("PP-301", "Cyclone Feed Slurry Pump", "Centrifugal Slurry Pump", new(2.30f, 0.0f, -7.05f), new(0.60f, 0.55f, 0.60f), 1.35f,
            "#1A5C96", "DN250 · 450 m³/h @ 2.8 bar · 160 kW",
            "Centrifugal pump at the mixing box base that lifts the coal-magnetite slurry up the PIPE-304 riser into DMC-301.",
            "Cyclone feed pump below the mixing box.",
            new[] { "MX-301" }, new[] { "PIPE-304" }, "DM-301"),
        SubNode("DMC-301", "Full-Length Wide Diamond Dense-Medium Cyclone", "Dense-Medium Cyclone", new(-1.95f, 3.72f, -7.60f), new(2.20f, 0.96f, 1.32f), 2.15f,
            "#D97706", "4.30 m × 2.58 m wide asymmetric diamond · 1.84 m tall East flat → 0.96 m West flat · SG 1.52",
            "Full-length flat-ended asymmetric diamond cyclone spanning across both 4.3 m wash screens: PIPE-304 enters the West side; clean coal (VF-301/PIPE-312) and discard (APEX-301/PIPE-313) are thrown from the East side into SC-301 and SC-302 underneath to be washed.",
            "Full-length wide asymmetric diamond cyclone with flat East/West ends on open-top gantry above the wash screens.",
            new[] { "PIPE-304", "PT-301" }, new[] { "VF-301", "APEX-301", "PIPE-312", "PIPE-313" }, "DM-301",
            glb: "dmc_cyclone_dmc301.glb"),
        SubNode("SC-301", "Clean Coal Product Drain & Rinse Screen", "Product Wash Screen", new(-1.95f, 1.95f, -6.80f), new(2.15f, 0.55f, 0.62f), 1.25f,
            "#1A5C96", "4.3 m × 1.24 m · 0.5 mm wedge-wire · 2 × 15 kW",
            "Drains and rinses magnetite off cyclone overflow clean coal before discharge onto CV-301.",
            "Product wash screen fed by DMC-301 overflow.",
            new[] { "DMC-301", "SPRAY-301" }, new[] { "CV-301", "BK-301" }, "DM-301"),
        SubNode("SC-302", "Discard / Refuse Drain & Rinse Screen", "Discard Wash Screen", new(-1.95f, 1.95f, -8.40f), new(2.15f, 0.55f, 0.62f), 1.25f,
            "#1A5C96", "4.3 m × 1.24 m · 0.5 mm wedge-wire · 2 × 15 kW",
            "Drains and rinses magnetite off cyclone underflow discard before discharge onto CV-302.",
            "Discard wash screen fed by DMC-301 underflow.",
            new[] { "DMC-301", "SPRAY-301" }, new[] { "CV-302", "BK-301" }, "CV-501"),
        SubNode("SPRAY-301", "Overhead Magnetite-Reclaim Water Sprayer Manifold", "Spray Bar Manifold", new(-2.15f, 2.85f, -7.60f), new(1.45f, 0.30f, 1.55f), 0.75f,
            "#B8C0C8", "5 spray bars · 30 nozzles · 3.5 bar",
            "Clarified water sprays wash adhering magnetite off product and discard so it drips into BK-301 for reclaim.",
            "Overhead spray bars spanning both wash screens.",
            new[] { "PIPE-307" }, new[] { "SC-301", "SC-302", "BK-301" }, "PP-203"),
        SubNode("BK-301", "Ground-Floor Under-Screen Tank & Magnetite Drip Bucket", "Drip Bucket / Sump", new(-1.90f, 0.15f, -7.65f), new(1.80f, 0.85f, 1.45f), 1.85f,
            "#3A4048", "3.6 m × 2.8 m under-screen tanks · conical sump",
            "Collects magnetite-laden rinse water dripping off both wash screens for pumping back to medium recovery.",
            "Ground-floor tanks and conical magnetite drip bucket.",
            new[] { "SC-301", "SC-302", "SPRAY-301" }, new[] { "PP-302" }, "MS-301"),
        SubNode("PP-302", "Magnetite Drip-Bucket Reclaim Pump", "Centrifugal Slurry Pump", new(-1.90f, 0.10f, -9.45f), new(0.65f, 0.50f, 0.55f), 1.15f,
            "#1A5C96", "DN150 · 180 m³/h @ 2.2 bar · 45 kW",
            "Pumps reclaimed magnetite rinse from BK-301 through PIPE-301 to MS-301.",
            "Reclaim pump beside the drip bucket.",
            new[] { "BK-301" }, new[] { "PIPE-301" }, "MS-301"),

        // Stage 1–2 (15)
        SubNode("GRIZ-101", "BN-101 Top Static Tramp-Rock Grizzly Bars", "Static Grizzly Deck", new(-13.80f, 7.80f, 8.80f), new(1.34f, 0.14f, 1.34f), 0.55f,
            "#64748B", "2.68 m × 2.68 m · 300 mm bar spacing",
            "Scalps oversized tramp rock and frozen lumps at the BN-101 hopper mouth before ROM coal enters the surge bin.",
            "Heavy-duty manganese grizzly bars spanning the top of BN-101.",
            new[] { "FEL-101" }, new[] { "BN-101" }, "AF-001", "Stage 1 — ROM Reception"),
        SubNode("RAMP-101", "ROM Concrete Loading Ramp & Wheel-Stop Bumper", "Loader Ramp & Stop", new(-13.80f, 4.35f, 11.10f), new(1.55f, 1.25f, 1.15f), 2.75f,
            "#94A3B8", "2.9 m wide × 2.3 m incline · +0.88 m rise · safety stop",
            "Supports FEL-101 wheel-loader climb onto the +5.23 m dump crest while the yellow bumper arrests tyre travel at the hopper rim.",
            "Reinforced concrete wing-wall loading ramp on the South side of BN-101.",
            new[] { "FEL-101" }, new[] { "GRIZ-101", "BN-101" }, "AF-001", "Stage 1 — ROM Reception"),
        SubNode("GATE-101", "BN-101 Discharge Throat & Isolation Gate", "Hopper Discharge Gate", new(-13.80f, 4.81f, 8.80f), new(0.52f, 0.55f, 0.52f), 1.30f,
            "#F59E0B", "1.02 m flange · manual/hydraulic rack gate",
            "Regulates coal head load exiting the BN-101 conical throat onto the primary vibrating grizzly feeder.",
            "Flanged bottom discharge throat beneath BN-101.",
            new[] { "BN-101" }, new[] { "CHUTE-100" }, "AF-001", "Stage 1 — ROM Reception"),
        SubNode("CHUTE-100", "BN-101 to VF-101 Feed Transfer Chute", "Transfer Chute", new(-13.80f, 5.20f, 7.65f), new(0.62f, 0.55f, 0.72f), 1.30f,
            "#475569", "1.24 m × 1.44 m AR-plate chute",
            "Guides gravity-discharged ROM coal from GATE-101 smoothly onto the rear impact pan of VF-101.",
            "Lined steel transfer chute bridging BN-101 and VF-101.",
            new[] { "GATE-101" }, new[] { "PAN-101", "VF-101" }, "AF-001", "Stage 1 — ROM Reception"),
        SubNode("GRIZ-102", "VF-101 Stepped Scalping Grizzly Bars", "Scalping Grizzly Section", new(-13.80f, 5.05f, 5.40f), new(0.65f, 0.20f, 0.45f), 0.65f,
            "#0284C7", "75 mm tapered manganese bars · 2-step cascade",
            "Separates <75 mm fines through the bar slots to CHUTE-102 (CV-102) while +75 mm lumps ride forward to CHUTE-101 (CV-101).",
            "Tapered self-cleaning grizzly bar section at the discharge end of VF-101.",
            new[] { "PAN-101" }, new[] { "CHUTE-101", "CHUTE-102" }, "AF-001", "Stage 2 — Initial Separation"),
        SubNode("PAN-101", "VF-101 Solid Manganese Impact Pan", "Feeder Trough Pan", new(-13.80f, 5.30f, 6.45f), new(0.68f, 0.35f, 0.78f), 0.95f,
            "#334155", "1.62 m × 1.26 m · 8° decline · manganese liner",
            "Absorbs ROM impact from BN-101 and spreads burden evenly across the trough width ahead of the grizzly bars.",
            "Heavy-duty wear-lined feeder pan section of VF-101.",
            new[] { "CHUTE-100" }, new[] { "GRIZ-102" }, "AF-001", "Stage 2 — Initial Separation"),
        SubNode("DRV-101", "VF-101 Dual Unbalanced Exciter Drive", "Vibrator Exciter Motors", new(-14.68f, 5.25f, 6.35f), new(0.30f, 0.28f, 0.35f), 0.75f,
            "#1D4ED8", "2 × 18.5 kW · counter-rotating eccentric weights",
            "Generates linear 45° throw vibration to convey ROM coal downhill across the pan and grizzly deck.",
            "Side-mounted eccentric vibrator motor assembly on VF-101.",
            new[] { "VF-101" }, new[] { "PAN-101", "GRIZ-102" }, "AF-001", "Stage 2 — Initial Separation"),
        SubNode("CHUTE-101", "VF-101 Oversize (+75 mm) Discharge Nose", "Oversize Feed Chute", new(-13.80f, 4.70f, 4.95f), new(0.65f, 0.30f, 0.35f), 0.80f,
            "#475569", "1.28 m wide · 22° wear-lined lip",
            "Directs +75 mm grizzly oversize coal onto the tail of Jaw Feed Conveyor CV-101.",
            "Bifurcated oversize discharge chute feeding CV-101.",
            new[] { "GRIZ-102" }, new[] { "CV-101" }, "CV-101", "Stage 2 — Initial Separation"),
        SubNode("CHUTE-102", "VF-101 Undersize (<75 mm) Bypass Hopper", "Undersize Chute", new(-13.10f, 4.55f, 5.80f), new(0.48f, 0.30f, 0.45f), 0.80f,
            "#475569", "1.04 m × 0.70 m tapered pan",
            "Collects scalped <75 mm fines dropping through GRIZ-102 and loads Undersize Bypass Conveyor CV-102.",
            "Under-grizzly fines collection hopper feeding CV-102.",
            new[] { "GRIZ-102" }, new[] { "CV-102" }, "CV-102", "Stage 2 — Initial Separation"),
        SubNode("BW-101", "CV-101 Primary Jaw-Feed Belt Weigher", "Belt Weightometer", new(-13.80f, 3.85f, 3.10f), new(0.62f, 0.35f, 0.45f), 0.90f,
            "#EAB308", "Dual-idler load cell · 0–650 t/h · ±0.5%",
            "Measures instantaneous +75 mm oversize mass flow feeding Primary Jaw Crusher JC-101.",
            "Strain-gauge belt scale bridge mounted mid-span on CV-101.",
            new[] { "CV-101" }, new[] { "CV-101" }, "CV-101", "Stage 2 — Initial Separation"),
        SubNode("BW-102", "CV-102 Undersize Bypass Belt Weigher", "Belt Weightometer", new(-10.08f, 4.18f, 4.86f), new(0.52f, 0.32f, 0.52f), 0.85f,
            "#EAB308", "Dual-idler load cell · 0–400 t/h · ±0.5%",
            "Measures scalped <75 mm bypass tonnage conveyed directly to Central Sizing Screen SS-101.",
            "Strain-gauge belt scale bridge mounted on CV-102.",
            new[] { "CV-102" }, new[] { "CV-102" }, "CV-102", "Stage 2 — Initial Separation"),
        SubNode("M-CV101", "CV-101 Head Pulley Drive Motor & Gearbox", "Conveyor Drive Unit", new(-14.55f, 3.10f, 0.85f), new(0.35f, 0.30f, 0.35f), 0.80f,
            "#1D4ED8", "55 kW TEFC motor · helical bevel reducer",
            "Drives the CV-101 head pulley at 2.6 m/s with regenerative braking on the -17° decline.",
            "Head-end shaft-mounted drive motor assembly on CV-101.",
            new[] { "CV-101" }, new[] { "JC-101" }, "CV-101", "Stage 2 — Initial Separation"),
        SubNode("M-CV102", "CV-102 Head Pulley Drive Motor & Gearbox", "Conveyor Drive Unit", new(-2.05f, 3.60f, 1.75f), new(0.35f, 0.30f, 0.35f), 0.80f,
            "#1D4ED8", "45 kW TEFC motor · helical bevel reducer",
            "Drives the CV-102 head pulley feeding undersize coal into SS-101 feed box.",
            "Head-end drive motor assembly on CV-102.",
            new[] { "CV-102" }, new[] { "FBOX-101", "SS-101" }, "CV-102", "Stage 2 — Initial Separation"),
        SubNode("RC-101", "CV-101 Emergency Pull-Wire Rip-Cord Switch", "Safety Pull-Cord", new(-14.35f, 4.05f, 2.20f), new(0.18f, 0.22f, 0.85f), 0.65f,
            "#EF4444", "Dual-side tensioned cable · manual reset trip",
            "Hardwired SIL-2 safety interlock immediately halting CV-101 and upstream VF-101 when pulled.",
            "Emergency stop pull-wire switch along CV-101 walkway.",
            new[] { "CV-101" }, new[] { "CV-101" }, "CV-101", "Stage 2 — Initial Separation"),
        SubNode("RC-102", "CV-102 Emergency Pull-Wire Rip-Cord Switch", "Safety Pull-Cord", new(-4.95f, 3.90f, 3.12f), new(0.75f, 0.22f, 0.22f), 0.65f,
            "#EF4444", "Dual-side tensioned cable · manual reset trip",
            "Hardwired SIL-2 safety interlock immediately halting CV-102 along the elevated bypass gantry.",
            "Emergency stop pull-wire switch along CV-102 walkway.",
            new[] { "CV-102" }, new[] { "CV-102" }, "CV-102", "Stage 2 — Initial Separation"),

        // Stage 3–5 (11)
        SubNode("FW-101", "JC-101 Twin Cast-Steel Eccentric Flywheels", "Crusher Flywheel Assembly", new(-13.80f, 1.92f, 0.46f), new(0.58f, 0.58f, 0.20f), 1.30f,
            "#EAB308", "Ø 1160 mm cast steel · 270 RPM · kinetic inertia",
            "Stores rotational kinetic energy from M-JC101 to smooth peak compressive jaw-crushing strokes.",
            "Heavy V-grooved inertia flywheel mounted on the JC-101 eccentric shaft.",
            new[] { "M-JC101" }, new[] { "JAW-101" }, "CR-101", "Stage 3 — Jaw Crushing"),
        SubNode("JAW-101", "JC-101 Fixed & Swing Manganese Jaw Plates", "Crushing Chamber Jaws", new(-13.22f, 1.95f, -0.60f), new(0.30f, 0.52f, 0.48f), 1.20f,
            "#475569", "1200 × 900 mm throat · CSS 110 mm · 18% Mn steel",
            "Compresses +75 mm ROM lumps between the corrugated fixed jaw and reciprocating swing jaw down to <110 mm.",
            "Replaceable corrugated manganese jaw liners inside the JC-101 throat.",
            new[] { "CV-101", "FW-101" }, new[] { "CHUTE-103" }, "CR-101", "Stage 3 — Jaw Crushing"),
        SubNode("M-JC101", "JC-101 315 kW Primary Crusher Drive Motor", "Main Drive Motor", new(-15.00f, 1.96f, -1.42f), new(0.40f, 0.30f, 0.40f), 0.85f,
            "#1D4ED8", "315 kW · 690 V · V-belt transmission",
            "Drives the eccentric shaft via guarded multi-rib V-belts with bearing temperature monitoring.",
            "Rear-skid electric drive motor powering JC-101.",
            new[] { "JC-101" }, new[] { "FW-101" }, "CR-101", "Stage 3 — Jaw Crushing"),
        SubNode("CHUTE-103", "JC-101 Under-Crusher Discharge Chute", "Crusher Discharge Chute", new(-13.70f, 0.48f, -0.60f), new(0.52f, 0.50f, 0.52f), 1.20f,
            "#334155", "1.04 m × 1.04 m skirted chute",
            "Funnel-feeds <110 mm jaw-crushed coal gently onto the tail of CV-103.",
            "Under-jaw discharge hopper feeding CV-103.",
            new[] { "JAW-101" }, new[] { "CV-103" }, "CR-101", "Stage 3 — Jaw Crushing"),
        SubNode("ROLL-101", "DRC-101 Counter-Rotating Toothed Double Rolls", "Toothed Crushing Rolls", new(-7.20f, 1.65f, -0.60f), new(0.78f, 0.30f, 0.72f), 0.85f,
            "#64748B", "Ø 760 mm × 1400 mm · pick-toothed segments",
            "Counter-rotating rolls nip and split +50 mm coal lumps with low fines generation and hydraulic tramp-iron relief.",
            "Dual counter-rotating toothed roll assembly inside DRC-101.",
            new[] { "HOP-101", "M-DRC101A", "M-DRC101B" }, new[] { "CHUTE-104" }, "CR-101", "Stage 4 — Double Roll Crushing"),
        SubNode("HOP-101", "DRC-101 Overhead Feed Surge Hopper", "Crusher Surge Hopper", new(-7.20f, 2.85f, -0.60f), new(1.15f, 0.70f, 1.00f), 1.65f,
            "#475569", "2.8 m × 2.4 m × 1.4 m · choke-feed buffer",
            "Buffers recirculated +50 mm oversize from CV-106 and diverted primary feed into an even ribbon across the rolls.",
            "Elevated steel surge hopper mounted directly above DRC-101 rolls.",
            new[] { "CV-106", "DIV-101" }, new[] { "ROLL-101" }, "CR-101", "Stage 4 — Double Roll Crushing"),
        SubNode("M-DRC101A", "DRC-101 Primary Roll Drive Motor A", "Roll Drive Motor", new(-8.07f, 1.62f, 0.42f), new(0.38f, 0.28f, 0.38f), 0.80f,
            "#1D4ED8", "90 kW TEFC motor · planetary gearbox",
            "Independently drives the West toothed roll shaft through a planetary speed reducer.",
            "South-West electric motor and planetary gearbox for DRC-101.",
            new[] { "DRC-101" }, new[] { "ROLL-101" }, "CR-101", "Stage 4 — Double Roll Crushing"),
        SubNode("M-DRC101B", "DRC-101 Secondary Roll Drive Motor B", "Roll Drive Motor", new(-6.33f, 1.62f, -1.62f), new(0.38f, 0.28f, 0.38f), 0.80f,
            "#1D4ED8", "90 kW TEFC motor · planetary gearbox",
            "Independently drives the East toothed roll shaft in counter-rotation.",
            "North-East electric motor and planetary gearbox for DRC-101.",
            new[] { "DRC-101" }, new[] { "ROLL-101" }, "CR-101", "Stage 4 — Double Roll Crushing"),
        SubNode("CHUTE-104", "DRC-101 Bottom Discharge Chute", "Crusher Discharge Chute", new(-7.20f, 0.45f, -0.60f), new(0.52f, 0.52f, 0.52f), 1.20f,
            "#334155", "0.90 m × 0.90 m skirted chute",
            "Directs secondary roll-crushed <50 mm coal onto the tail of Sizing Screen Feed Conveyor CV-104.",
            "Under-roll discharge chute feeding CV-104.",
            new[] { "ROLL-101" }, new[] { "CV-104" }, "CR-101", "Stage 4 — Double Roll Crushing"),
        SubNode("DIV-101", "CV-103 Head Flop-Gate Diverter (Bypass / DRC)", "Diverter Flap Gate", new(-8.15f, 2.35f, -0.55f), new(0.48f, 0.45f, 0.48f), 1.10f,
            "#F59E0B", "Pneumatic flop-gate · Bypass / Split / Crush",
            "Routes <110 mm CV-103 primary crushed discharge either directly onto CV-104 or into HOP-101 for secondary roll crushing.",
            "Motorised two-way diverter gate at the CV-103 discharge tower.",
            new[] { "CV-103" }, new[] { "HOP-101", "CV-104" }, "CV-101", "Stage 5 — Secondary Transfer"),
        SubNode("CHUTE-106", "SS-101 Curved Oversize Return Entry Chute", "Curved Return Chute", new(2.18f, 2.45f, -0.80f), new(0.52f, 0.45f, 1.05f), 1.10f,
            "#475569", "90° curved ceramic-lined chute",
            "Redirects +50 mm top-deck oversize from DECK-101A onto closed-circuit return conveyor CV-106.",
            "Curved transfer chute turning East screen discharge North-West onto CV-106.",
            new[] { "DECK-101A", "SS-101" }, new[] { "CV-106" }, "CV-102", "Stage 4 — Double Roll Crushing"),

        // Stage 6 (8)
        SubNode("FBOX-101", "SS-101 Dual-Inlet Convergence Feed Box", "Screen Feed Box", new(-1.85f, 3.55f, 0.50f), new(0.55f, 0.50f, 0.95f), 1.20f,
            "#334155", "1.85 m wide · dual stone-box inlets (CV-102 & CV-104)",
            "Combines scalped <75 mm bypass (CV-102) and crushed secondary feed (CV-104) uniformly across the full SS-101 screen width.",
            "Twin-inlet dead-bed stone-box atop the West end of SS-101.",
            new[] { "CV-102", "CV-104" }, new[] { "DECK-101A" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("DECK-101A", "SS-101 Upper 50 mm Scalping Screen Deck", "Top Screen Deck (50 mm)", new(0.08f, 2.65f, 0.50f), new(1.65f, 0.15f, 0.78f), 0.55f,
            "#0284C7", "3.35 m × 1.56 m · 50 mm punched/poly panels",
            "Retains +50 mm oversize lumps for closed-circuit recirculation via CHUTE-106 and CV-106 while passing <50 mm coal to DECK-101B.",
            "Coarse top screening deck inside Central Sizing Screen SS-101.",
            new[] { "FBOX-101" }, new[] { "CHUTE-106", "DECK-101B" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("DECK-101B", "SS-101 Lower 6 mm Sizing Screen Deck", "Lower Screen Deck (6 mm)", new(0.22f, 2.02f, 0.50f), new(1.78f, 0.22f, 0.78f), 0.65f,
            "#0EA5E9", "3.65 m × 1.56 m · 6 mm woven-wire / poly panels",
            "Classifies 6–50 mm middlings onto CHUTE-105 (CV-201 plant feed) while allowing <6 mm dry fines to drop into HOP-102.",
            "Fine sizing lower deck inside Central Sizing Screen SS-101.",
            new[] { "DECK-101A" }, new[] { "CHUTE-105", "HOP-102" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("DRV-102", "SS-101 Dual-Shaft Eccentric Vibrator Drive", "Screen Exciter Drive", new(0.25f, 3.45f, -0.74f), new(0.32f, 0.28f, 0.52f), 0.78f,
            "#EAB308", "55 kW drive · cardan-coupled eccentric pods",
            "Drives circular/elliptical vibration across the multi-deck screen body to stratify and convey coal.",
            "Overhead exciter motor and cardan drive pod on SS-101.",
            new[] { "SS-101" }, new[] { "DECK-101A", "DECK-101B" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("HOP-102", "SS-101 Under-Screen <6 mm Fines Hopper", "Under-Screen Fines Hopper", new(0.25f, 0.38f, 0.50f), new(0.95f, 0.55f, 0.65f), 1.25f,
            "#334155", "1.9 m × 1.3 m tapered fines hopper",
            "Collects <6 mm dry fines passing DECK-101B and discharges them onto Fines Takeaway Conveyor CV-105.",
            "Dust-skirted underpan hopper beneath SS-101.",
            new[] { "DECK-101B" }, new[] { "CV-105" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("VT-102", "SS-101 Tri-Axial Vibration & Bearing Transmitter", "Vibration Sensor", new(0.25f, 2.40f, 1.38f), new(0.22f, 0.20f, 0.18f), 0.60f,
            "#10B981", "4–20 mA / IO-Link · stroke 8–12 mm · bearing temp",
            "Continuously monitors screen G-force, stroke symmetry, and exciter bearing condition to detect broken springs or pegging.",
            "Side-frame vibration transducer mounted on SS-101.",
            new[] { "DRV-102", "SS-101" }, new[] { "SS-101" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("CHUTE-105", "SS-101 Plant-Feed (6–50 mm) Discharge Chute", "Plant-Feed Chute", new(2.32f, 1.45f, 0.50f), new(0.42f, 0.30f, 0.76f), 0.85f,
            "#475569", "1.52 m wide · 20° wear-lined discharge lip",
            "Transfers sized 6–50 mm middlings from DECK-101B onto Plant Feed Conveyor CV-201.",
            "East middlings discharge lip feeding CV-201.",
            new[] { "DECK-101B" }, new[] { "CV-201" }, "SC-102", "Stage 6 — Central Sizing Screen"),
        SubNode("BW-201", "CV-201 Wash Plant Feed Belt Weigher", "Belt Weightometer", new(6.45f, 2.65f, -0.20f), new(0.55f, 0.35f, 0.55f), 0.90f,
            "#EAB308", "Dual-idler load cell · 0–550 t/h · ±0.5%",
            "Measures dry sized plant feed entering Surge Bin BN-201 for dense-medium yield mass-balance accounting.",
            "Strain-gauge belt scale mounted mid-span on CV-201.",
            new[] { "CV-201" }, new[] { "BN-201" }, "CV-101", "Stage 6 — Central Sizing Screen"),

        // Wash Plant & Thickener (20)
        SubNode("GATE-201", "BN-201 Pneumatic Slide Gate & Actuator", "Pneumatic Slide Gate", new(12.05f, 0.45f, -0.80f), new(0.72f, 0.32f, 0.42f), 0.85f,
            "#F59E0B", "860 × 720 mm knife gate · 6 bar pneumatic cylinder",
            "Modulates coal drawdown from Surge Bin BN-201 onto Desliming Feed Conveyor CV-202.",
            "Bottom discharge slide gate and actuator beneath BN-201.",
            new[] { "BN-201" }, new[] { "CV-202" }, "SL-601", "Wash Plant & Medium Recovery"),
        SubNode("SPRAY-201", "DS-201 Overhead Desliming Spray-Bar Manifold", "Spray Bar Manifold", new(8.70f, 2.25f, -9.75f), new(1.30f, 0.40f, 0.90f), 1.00f,
            "#38BDF8", "4 spray headers · 16 fan nozzles · 3.5 bar",
            "High-velocity clarified water jets fluidise clay slimes through the 0.5 mm wedge-wire deck before dense-medium washing.",
            "Overhead spray-water header and nozzle bars above DS-201.",
            new[] { "PIPE-308", "VALVE-204" }, new[] { "DS-201", "PAN-201" }, "PP-203", "Wash Plant & Medium Recovery"),
        SubNode("PAN-201", "DS-201 Underpan Slimes Collection Hopper", "Slimes Underpan", new(8.75f, 0.24f, -9.80f), new(1.15f, 0.58f, 0.85f), 1.35f,
            "#334155", "2.3 m × 1.7 m · DN160 drain outlet",
            "Captures <0.5 mm slimes and wash water passing DS-201 for gravity flow via PIPE-310 to TK-201.",
            "Rubber-lined slurry collection underpan beneath DS-201.",
            new[] { "DS-201", "SPRAY-201" }, new[] { "PIPE-310" }, "SC-102", "Wash Plant & Medium Recovery"),
        SubNode("VALVE-204", "DS-201 Desliming Spray Water Isolation Valve", "Actuated Butterfly Valve", new(7.45f, 0.70f, -11.50f), new(0.30f, 0.32f, 0.30f), 0.80f,
            "#EF4444", "DN125 wafer butterfly · spring-return actuator",
            "Automatically isolates clarified spray water to DS-201 whenever the desliming screen or CV-202 stops.",
            "Automated spray-water isolation valve on PIPE-308.",
            new[] { "PIPE-308" }, new[] { "SPRAY-201" }, "PP-203", "Wash Plant & Medium Recovery"),
        SubNode("RAKE-201", "TK-201 Rotating Central Rake & Truss Bridge", "Thickener Rake Bridge", new(-1.20f, 1.80f, -15.80f), new(2.85f, 0.50f, 0.42f), 1.20f,
            "#EAB308", "Ø 5.4 m truss rake · torque-protected drive head",
            "Slowly rotates submerged rake arms to sweep settled clay slimes into the central cone while preventing torque overload.",
            "Overhead steel truss bridge and central rake drive atop TK-201.",
            new[] { "TK-201" }, new[] { "PP-201", "PP-202" }, "TK-201", "Wash Plant & Medium Recovery"),
        SubNode("LAUNDER-201", "TK-201 Peripheral Clarified Water Overflow Launder", "Overflow Launder", new(1.35f, 1.42f, -14.40f), new(0.55f, 0.25f, 0.55f), 0.70f,
            "#0EA5E9", "Ø 5.9 m V-notch weir · clarified water takeoff",
            "Skims clarified process water over peripheral V-notch weirs for recirculation by PP-203.",
            "Peripheral overflow launder rim and takeoff box on TK-201.",
            new[] { "TK-201" }, new[] { "PP-203" }, "TK-201", "Wash Plant & Medium Recovery"),
        SubNode("LT-201", "TK-201 Ultrasonic Clarified / Bed Level Transmitter", "Level Transmitter", new(-0.55f, 2.05f, -15.25f), new(0.20f, 0.24f, 0.20f), 0.65f,
            "#10B981", "4–20 mA ultrasonic · 0–100% mud-bed & pool",
            "Monitors thickener pool level and sludge-bed interface to modulate PP-201 underflow speed and PP-203 flocculant rate.",
            "Bridge-mounted ultrasonic level sensor on TK-201.",
            new[] { "TK-201" }, new[] { "PP-201", "PP-202" }, "TK-201", "Wash Plant & Medium Recovery"),
        SubNode("VALVE-201", "PP-201 Duty Underflow Suction Isolation Valve", "Slurry Knife-Gate Valve", new(-3.85f, 0.25f, -15.00f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#0EA5E9", "DN150 urethane-lined knife gate",
            "Isolates Duty Underflow Pump PP-201 from the TK-201 underflow cone for maintenance or pump changeover.",
            "Suction isolation knife-gate valve on PP-201.",
            new[] { "TK-201" }, new[] { "PP-201" }, "PP-201", "Wash Plant & Medium Recovery"),
        SubNode("VALVE-202", "PP-202 Standby Underflow Suction Isolation Valve", "Slurry Knife-Gate Valve", new(-3.85f, 0.25f, -16.30f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#64748B", "DN150 urethane-lined knife gate",
            "Isolates Standby Underflow Pump PP-202 from the TK-201 cone during duty operation on PP-201.",
            "Suction isolation knife-gate valve on PP-202.",
            new[] { "TK-201" }, new[] { "PP-202" }, "PP-202", "Wash Plant & Medium Recovery"),
        SubNode("VALVE-203", "PP-203 Flocculant Dosing Control Valve", "Modulating Control Valve", new(0.65f, 1.90f, -15.80f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#10B981", "DN80 diaphragm control valve · 4–20 mA",
            "Trims anionic polymer flocculant addition into the TK-201 feedwell based on slimes feed turbidity.",
            "Automated flocculant dosing control valve on PIPE-309.",
            new[] { "PIPE-309" }, new[] { "TK-201" }, "PP-203", "Wash Plant & Medium Recovery"),
        SubNode("AGIT-301", "MX-301 Top-Mounted Mixing Box Agitator Drive", "Tank Agitator Drive", new(2.30f, 2.30f, -8.30f), new(0.42f, 0.32f, 0.42f), 0.85f,
            "#1D4ED8", "22 kW vertical gearmotor · 3-blade hydrofoil",
            "Keeps magnetite and 6–50 mm coal uniformly suspended inside MX-301 ahead of cyclone feed pump PP-301.",
            "Bridge-mounted agitator motor and reducer atop MX-301.",
            new[] { "WP-301" }, new[] { "MX-301" }, "DM-301", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("DT-301", "PIPE-303 Nuclear/Coriolis Medium Density Transmitter", "SG Density Transmitter", new(-2.50f, 1.25f, -10.20f), new(0.26f, 0.24f, 0.26f), 0.70f,
            "#14B8A6", "SG 1.20–2.00 range · ±0.002 SG accuracy",
            "Measures correct-medium specific gravity (target SG 1.52) feeding MX-301 to drive closed-loop water dilution.",
            "Inline medium density gauge clamped onto PIPE-303.",
            new[] { "PIPE-303" }, new[] { "MX-301" }, "TK-301", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("VALVE-303", "MX-301 Clarified Makeup Water Control Valve", "Modulating Control Valve", new(2.45f, 0.95f, -9.60f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#0EA5E9", "DN100 modulating butterfly valve",
            "Adds clarified dilution water into MX-301 to maintain target cyclone feed SG at 1.52.",
            "Clarified water dilution control valve at the MX-301 North inlet.",
            new[] { "PIPE-307" }, new[] { "MX-301" }, "PP-203", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("AGIT-302", "TK-301 Top-Mounted Magnetite Tank Agitator", "Tank Agitator Drive", new(-5.40f, 2.52f, -12.00f), new(0.45f, 0.45f, 0.45f), 1.10f,
            "#1D4ED8", "30 kW vertical gearmotor · heavy-duty impeller",
            "Prevents high-density magnetite medium from settling on the floor of sump tank TK-301.",
            "Bridge-mounted agitator gearbox and motor atop TK-301.",
            new[] { "TK-301" }, new[] { "PIPE-303" }, "TK-301", "Wash Plant & Medium Recovery"),
        SubNode("PIPE-312", "DMC-301 Clean Coal Overflow Drop Pipe to SC-301", "Cyclone Overflow Pipe", new(0.45f, 3.30f, -6.80f), new(0.42f, 0.72f, 0.32f), 1.05f,
            "#EAB308", "DN340 ceramic-lined bend · East side to SC-301",
            "Throws low-density clean coal float and medium from the East side of DMC-301 (VF-301) down into Product Wash Screen SC-301 to be washed.",
            "Yellow-flanged overflow pipe dropping from East side of DMC-301 into SC-301.",
            new[] { "DMC-301", "VF-301" }, new[] { "SC-301" }, "DM-301", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("PIPE-313", "DMC-301 Discard Underflow Drop Pipe to SC-302", "Cyclone Underflow Pipe", new(0.45f, 3.25f, -8.40f), new(0.42f, 0.70f, 0.32f), 1.00f,
            "#475569", "DN300 ceramic-lined bend · East side to SC-302",
            "Throws high-density shale/rock sinks from the East side of DMC-301 (APEX-301) down into Discard Wash Screen SC-302 to be washed.",
            "Ceramic-lined underflow pipe dropping from East side of DMC-301 into SC-302.",
            new[] { "DMC-301", "APEX-301" }, new[] { "SC-302" }, "CV-501", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("VF-301", "DMC-301 East Vortex Finder (Product Outlet)", "Cyclone Vortex Finder", new(0.46f, 4.20f, -6.92f), new(0.32f, 0.45f, 0.32f), 1.10f,
            "#EAB308", "DN340 ceramic vortex finder · East Product side",
            "Extracts separated low-density clean coal & medium from the East side of DMC-301 into PIPE-312 above Product Screen SC-301.",
            "Vortex finder housing on the East Product side of DMC-301.",
            new[] { "DMC-301" }, new[] { "PIPE-312" }, "DM-301", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("APEX-301", "DMC-301 East Discard Spigot Outlet", "Cyclone Apex Spigot", new(0.46f, 4.10f, -8.28f), new(0.28f, 0.40f, 0.28f), 0.95f,
            "#3A4048", "DN300 silicon-carbide spigot · East Discard side",
            "Discharges dense shale/refuse sinks from the East side of DMC-301 into PIPE-313 above Discard Screen SC-302.",
            "Bolted discard spigot nozzle on the East Discard side of DMC-301.",
            new[] { "DMC-301" }, new[] { "PIPE-313" }, "CV-501", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("VALVE-301", "SC-301 Under-Screen Product Medium Drain Valve", "Medium Drain Valve", new(0.15f, 1.05f, -6.55f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#14B8A6", "DN150 rubber-lined pinch/knife valve",
            "Balances drained medium partitioning from the SC-301 product under-tank into the BK-301 drip bucket.",
            "Drain control valve on the SC-301 under-screen collection tank.",
            new[] { "SC-301" }, new[] { "BK-301" }, "DM-301", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("VALVE-302", "SC-302 Under-Screen Discard Medium Drain Valve", "Medium Drain Valve", new(0.15f, 1.05f, -8.75f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#14B8A6", "DN150 rubber-lined pinch/knife valve",
            "Balances drained medium partitioning from the SC-302 discard under-tank into the BK-301 drip bucket.",
            "Drain control valve on the SC-302 under-screen collection tank.",
            new[] { "SC-302" }, new[] { "BK-301" }, "CV-501", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("PT-301", "DMC-301 West Feed Inlet Pressure Transmitter", "Pressure Transmitter", new(-4.60f, 5.18f, -8.05f), new(0.24f, 0.24f, 0.24f), 0.72f,
            "#10B981", "0–400 kPa diaphragm seal · target 185 kPa",
            "Monitors tangential cyclone inlet head pressure on the PIPE-304 top-side inlet manifold just ahead of DMC-301 to ensure sharp SG 1.52 separation cutpoint.",
            "Diaphragm-isolated pressure transmitter mounted on PIPE-304 right before the DMC-301 inlet.",
            new[] { "PP-301", "PIPE-304" }, new[] { "DMC-301" }, "DM-301", "Wash Plant & Medium Recovery (WP-301)"),
        SubNode("VALVE-305", "WP-301 Clarified Spray Header Isolation Valve", "Actuated Butterfly Valve", new(-0.65f, 2.92f, -9.28f), new(0.24f, 0.24f, 0.24f), 0.68f,
            "#38BDF8", "DN100 actuated butterfly valve",
            "Shuts off clarified rinse water to the 5 overhead WP-301 spray bars when wash screens are stopped.",
            "Isolation valve feeding the overhead SPRAY-301 manifold.",
            new[] { "PIPE-307" }, new[] { "SPRAY-301" }, "PP-203", "Wash Plant & Medium Recovery (WP-301)"),

        // Specialized 3D GLB Process & Infrastructure Sub-Equipment (7)
        SubNode("OM-101", "CV-101 Cross-Belt Self-Cleaning Tramp Iron Magnet", "Overband Tramp Magnet", new(-13.80f, 3.35f, 1.35f), new(0.85f, 0.85f, 0.75f), 2.10f,
            "#DC2626", "1.4 m × 2.0 m cross-belt · 950 Gauss · self-cleaning",
            "Extracts ferromagnetic roof bolts, drill steel and bucket teeth from +75 mm ROM lumps on CV-101 ahead of Primary Jaw Crusher JC-101.",
            "Gantry-suspended self-cleaning overband magnet straddling CV-101 with tramp-iron discard chute.",
            new[] { "VF-101", "CV-101" }, new[] { "JC-101" }, "CV-101", "Stage 2 — Initial Separation",
            glb: "overband_magnet_om101.glb"),
        SubNode("DC-101", "Crushing & Screening Pulse-Jet Baghouse Dust Collector", "Baghouse Dust Collector", new(-9.80f, 0.0f, 3.60f), new(1.25f, 1.85f, 1.15f), 4.05f,
            "#F59E0B", "24,000 Nm³/h · 144 PTFE filter bags · <10 mg/Nm³",
            "Draws fugitive coal dust from JC-101, DRC-101 and SS-101 transfer hoods through pulse-jet filter bags with rotary airlock fines return.",
            "Pulse-jet baghouse dust extraction unit with twin pyramidal hoppers, ID fan and clean-air stack.",
            new[] { "JC-101", "DRC-101", "SS-101" }, new[] { "CV-105" }, "SC-102", "Stage 6 — Central Sizing Screen",
            glb: "dust_collector_dc101.glb"),
        SubNode("CF-501", "Clean Coal Vibratory Screen-Bowl Centrifuge", "Screen-Bowl Centrifuge", new(-5.40f, 0.0f, -4.10f), new(1.40f, 1.15f, 1.05f), 2.55f,
            "#D97706", "Ø 1100 × 2200 mm bowl · 195 kW · 650 G centrifugal",
            "Mechanically dewaters washed clean coal from WP-301 / SC-301 down to 8.6% surface moisture prior to CV-301 product stacking.",
            "High-G vibratory screen-bowl dewatering centrifuge on spring-isolated inertia skid.",
            new[] { "WP-301", "SC-301" }, new[] { "CV-301" }, "CF-501", "Wash Plant & Medium Recovery",
            glb: "centrifuge_dryer_cf501.glb"),
        SubNode("AN-601", "CV-301 Cross-Belt PGNAA Online Coal Quality Analyzer", "PGNAA Elemental Analyzer", new(-6.80f, 0.65f, -5.20f), new(1.15f, 1.35f, 1.15f), 2.85f,
            "#F59E0B", "Cf-252 / BGO detector · real-time Ash %, Moisture % & CV",
            "Continuously scans the full cross-section of clean product coal on CV-301 via Prompt Gamma Neutron Activation Analysis every 60 seconds.",
            "Straddle-mounted radiation-shielded PGNAA analyzer bridge over Clean Product Conveyor CV-301.",
            new[] { "CV-301", "CF-501" }, new[] { "TLS-401", "SP-301" }, "AN-601", "Wash Plant & Medium Recovery",
            glb: "pgnaa_analyzer_an601.glb"),
        SubNode("FP-501", "Tailings Recessed-Plate High-Pressure Filter Press", "Tailings Filter Press", new(-8.80f, 0.0f, -14.80f), new(2.15f, 1.38f, 1.15f), 2.95f,
            "#38BDF8", "120 recessed plates · 1500 × 1500 mm · 16 bar squeeze",
            "Dewaters thickened TK-201 underflow slurry from PP-201/PP-202 into dry handleable filter cake (<19% moisture) while returning filtrate water.",
            "Automatic recessed-plate tailings filter press closing the zero-liquid-discharge water loop.",
            new[] { "TK-201", "PP-201", "PP-202" }, new[] { "CV-302", "SP-302" }, "FP-501", "Wash Plant & Medium Recovery",
            glb: "filter_press_fp501.glb"),
        SubNode("TLS-401", "Rapid Rail & Haul-Truck Flood-Loading Loadout Silo", "Rapid Loadout Silo", new(-12.60f, 0.0f, -4.80f), new(1.60f, 2.75f, 1.60f), 5.75f,
            "#F59E0B", "1,850 t/h batch weigh-bin · telescopic flood chute",
            "Buffers certified clean product coal from SP-301 / CV-301 and flood-loads unit-train wagons and highway haul trucks via dual load-cell batch gates.",
            "Drive-through steel portal rapid loadout tower with conical mass-flow surge silo and telescopic chute.",
            new[] { "CV-301", "SP-301", "AN-601" }, Array.Empty<string>(), "LD-601", "Wash Plant & Medium Recovery",
            glb: "train_loadout_silo_tls401.glb"),
        SubNode("MCC-101", "CHPP Containerized E-House Substation & 3.3 MVA Transformer", "MCC E-House & Substation", new(6.80f, 0.0f, 4.20f), new(2.35f, 1.45f, 1.45f), 3.15f,
            "#D97706", "11 kV / 690 V · 3.3 MVA oil transformer · VFD & PLC lineup",
            "Supplies 690 V/400 V motor control centers, VFD regenerative drives and dual-redundant Modbus TCP PLC racks across all 6 CHPP stages.",
            "Pressurized E-House electrical substation with HVAC units and bunded outdoor step-down transformer.",
            Array.Empty<string>(), new[] { "JC-101", "DRC-101", "SS-101", "WP-301", "TK-201" }, "CR-101", "Stage 6 — Central Sizing Screen",
            glb: "mcc_substation_mcc101.glb")
    };

    public static readonly IReadOnlyList<Plant3DNode> PipeNodes = BuildPipeNodes();

    /// <summary>All individually interactive 3D items: 29 GA nodes + 71 sub-equipment + 11 process pipes (111 total).</summary>
    public static readonly IReadOnlyList<Plant3DNode> InteractiveNodes = AllNodes.Concat(SubEquipmentNodes).Concat(PipeNodes).ToArray();

    private static Plant3DNode SubNode(
        string id, string ga, string type, Vector3 pos, Vector3 half, float labelH, string color,
        string spec, string principle, string desc, string[] up, string[] down, string scada,
        string stage = "Wash Plant & Medium Recovery (WP-301)",
        string glb = "",
        float yaw = 0f) =>
        new(
            Id: id,
            GaLabel: ga,
            StageLabel: stage,
            EquipmentType: type,
            GlbFileName: glb,
            Position: pos,
            YawDegrees: yaw,
            Scale: Vector3.One,
            HalfExtents: half,
            LabelHeight: labelH,
            DefaultColor: Color.Parse(color),
            DimensionsSpec: spec,
            MechanicalPrinciple: principle,
            ComponentDescription: desc,
            UpstreamIds: up,
            DownstreamIds: down,
            LinkedScadaId: scada,
            IsSubComponent: true);

    private static IReadOnlyList<Plant3DNode> BuildPipeNodes()
    {
        var list = new List<Plant3DNode>(PipeRoutes.Count);
        foreach (var r in PipeRoutes)
        {
            if (string.IsNullOrWhiteSpace(r.Id)) continue;
            var mid = SamplePolyline(r.Waypoints, 0.5f);
            var minV = r.Waypoints.Aggregate(Vector3.Min);
            var maxV = r.Waypoints.Aggregate(Vector3.Max);
            var half = Vector3.Max((maxV - minV) * 0.5f, new Vector3(0.2f));
            string scada = r.Id switch
            {
                "PIPE-303" => "TK-301",
                "PIPE-305" => "TK-201",
                "PIPE-308" => "PP-203",
                "PIPE-309" => "PP-203",
                "PIPE-310" => "SC-102",
                "PIPE-311" => "PP-202",
                _ => r.SourceEquipmentId
            };
            list.Add(new Plant3DNode(
                Id: r.Id,
                GaLabel: r.GaLabel,
                StageLabel: r.StageLabel,
                EquipmentType: $"Process Pipe ({r.Medium})",
                GlbFileName: string.Empty,
                Position: mid,
                YawDegrees: 0f,
                Scale: Vector3.One,
                HalfExtents: half,
                LabelHeight: 0.45f,
                DefaultColor: GetMediumColor(r.Medium),
                DimensionsSpec: r.DimensionsSpec,
                MechanicalPrinciple: r.MechanicalPrinciple,
                ComponentDescription: r.ComponentDescription,
                UpstreamIds: r.UpstreamIds ?? Array.Empty<string>(),
                DownstreamIds: r.DownstreamIds ?? Array.Empty<string>(),
                LinkedScadaId: scada,
                IsPipe: true));
        }
        return list;
    }

    public static PipeRoute? FindPipeRoute(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var r in PipeRoutes)
        {
            if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
                return r;
        }
        return null;
    }

    private static readonly object CacheLock = new();
    private static Dictionary<string, GlbMeshData>? _meshCache;

    public static IReadOnlyDictionary<string, GlbMeshData> LoadAllGlbMeshes()
    {
        lock (CacheLock)
        {
            if (_meshCache is not null)
                return _meshCache;

            var cache = new Dictionary<string, GlbMeshData>(StringComparer.OrdinalIgnoreCase);
            var exportDir = FindAssetsExportDirectory();
            if (exportDir is not null && Directory.Exists(exportDir))
            {
                // Load all exported .glb files (including sub-parts like wheel_loader_bucket.glb).
                // Per-file catch: one bad mesh won't abort the entire GPU init.
                foreach (var fullPath in Directory.EnumerateFiles(exportDir, "*.glb"))
                {
                    var fileName = Path.GetFileName(fullPath);
                    if (cache.ContainsKey(fileName))
                        continue;

                    try
                    {
                        var mesh = GlbMeshLoader.Load(fullPath);
                        if (mesh is not null)
                            cache[fileName] = mesh;
                    }
                    catch
                    {
                        // Skip malformed GLB; the scene still renders with fallback primitives.
                    }
                }
            }

            _meshCache = cache;
            return _meshCache;
        }
    }

    public static void InvalidateMeshCache()
    {
        lock (CacheLock)
        {
            _meshCache = null;
        }
    }

    public static string? FindAssetsExportDirectory()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "3D", "Exported");
        if (Directory.Exists(bundled))
            return bundled;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "ArchScada", "Assets", "3D", "Exported");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the 4-stage articulated wheel loader pose for FEL-101 atop the +4.35 m ROM hill:
    ///   [0.00..0.14): Scoop ROM at SP-001 (Z = 14.3, Y = 4.35)
    ///   [0.14..0.32): Drive North and climb BN-101 ramp (Z = 14.3 -> 11.75, Y = 4.35 -> 5.23) while lifting boom (0 -> 38 deg)
    ///   [0.32..0.48]: Dump raw coal boulders into BN-101 (boom 38 deg, bucket dump 0 -> -58 deg)
    ///   [0.48..0.74): Reverse down ramp (Z = 11.75 -> 14.3, Y = 5.23 -> 4.35) and lower boom
    ///   [0.74..1.00): Turn & reposition at SP-001 for next scoop
    /// </summary>
    public static LoaderPose ComputeLoaderPose(double simTime, bool active)
    {
        const float yHillBase = 4.35f;
        const float yRampRise = 0.88f;

        if (!active)
        {
            return new LoaderPose(
                ChassisPosition: new(-13.8f, yHillBase, 11.8f),
                YawDegrees: 90f,
                PitchDegrees: 0f,
                BoomAngleDegrees: 8f,
                BucketDumpAngleDegrees: 0f,
                CyclePhase: 0f,
                IsDumpingCoal: false,
                DumpIntensity: 0f,
                BinFillPulse: 0f);
        }

        if (!double.IsFinite(simTime))
            simTime = 0.0;

        const double cycleSeconds = 12.0;
        double wrapped = (simTime % cycleSeconds);
        if (wrapped < 0) wrapped += cycleSeconds;
        float t = (float)(wrapped / cycleSeconds);

        const float x = -13.8f;
        const float zStockpile = 14.3f;
        const float zRampTop = 11.75f;
        const float zRampToe = 13.1f;

        float z;
        float y;
        float yaw;
        float pitch;
        float boom;
        float dump;
        bool dumping = false;
        float dumpIntensity = 0f;

        if (t < 0.14f)
        {
            // Stage 1: Scoop at SP-001 (facing South +Z, yaw -90)
            float u = Smooth01(t / 0.14f);
            z = zStockpile + MathF.Sin(u * MathF.PI) * 0.25f;
            y = yHillBase;
            yaw = -90f;
            pitch = 0f;
            boom = 4f + u * 6f;
            dump = 12f * (1f - u);
        }
        else if (t < 0.32f)
        {
            // Stage 2: Turn toward BN-101 (North -Z, yaw +90) and climb ramp to Z=11.75, Y=5.23
            float u = Smooth01((t - 0.14f) / 0.18f);
            float turnU = Smooth01(Math.Clamp((t - 0.14f) / 0.08f, 0f, 1f));
            z = Lerp(zStockpile, zRampTop, u);
            float rampFrac = Math.Clamp((zRampToe - z) / (zRampToe - zRampTop), 0f, 1f);
            y = yHillBase + rampFrac * yRampRise;
            yaw = Lerp(-90f, 90f, turnU);
            pitch = rampFrac * 14f;
            boom = Lerp(10f, 38f, u);
            dump = 0f;
        }
        else if (t <= 0.48f)
        {
            // Stage 3: Dump into BN-101 hopper (chassis halted at wheel-stop beam, bucket overhanging hopper)
            float u = (t - 0.32f) / 0.16f;
            z = zRampTop;
            y = yHillBase + yRampRise;
            yaw = 90f;
            pitch = 14f;
            boom = 38f;
            float dumpCurve = MathF.Sin(u * MathF.PI);
            dump = -58f * Math.Clamp(Smooth01(Math.Min(u * 2.2f, 1f)) * (u < 0.85f ? 1f : (1f - (u - 0.85f) / 0.15f)), 0f, 1f);
            if (u is >= 0.18f and <= 0.78f) dump = -58f;
            dumping = true;
            dumpIntensity = dumpCurve;
        }
        else if (t < 0.74f)
        {
            // Stage 4: Reverse down ramp to stockpile staging Z=14.3, Y=4.35
            float u = Smooth01((t - 0.48f) / 0.26f);
            z = Lerp(zRampTop, zStockpile, u);
            float rampFrac = Math.Clamp((zRampToe - z) / (zRampToe - zRampTop), 0f, 1f);
            y = yHillBase + rampFrac * yRampRise;
            yaw = 90f;
            pitch = rampFrac * 14f;
            boom = Lerp(38f, 6f, u);
            dump = Lerp(-58f, 0f, Math.Clamp(u * 1.6f, 0f, 1f));
        }
        else
        {
            // Reposition & turn South toward SP-001 ready to scoop
            float u = Smooth01((t - 0.74f) / 0.26f);
            z = zStockpile;
            y = yHillBase;
            yaw = Lerp(90f, -90f, u);
            pitch = 0f;
            boom = Lerp(6f, 4f, u);
            dump = Lerp(0f, 12f, u);
        }

        float binPulse = dumping ? 0.14f * dumpIntensity : 0f;
        return new LoaderPose(
            ChassisPosition: new(x, y, z),
            YawDegrees: yaw,
            PitchDegrees: pitch,
            BoomAngleDegrees: boom,
            BucketDumpAngleDegrees: dump,
            CyclePhase: t,
            IsDumpingCoal: dumping,
            DumpIntensity: dumpIntensity,
            BinFillPulse: binPulse);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Smooth01(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// Authoritative PLC / SCADA channel mapping sourced from Projects/ArchCoalPlant/BaseXML/Device.xml &amp; Cnl.xml.
    /// </summary>
    public static string GetPlcChannelMapping(string nodeId, string linkedScadaId) => nodeId.ToUpperInvariant() switch
    {
        "BN-101" or "GRIZ-101" or "RAMP-101" or "GATE-101" or "CHUTE-100" => "Direct PLC: ROM101 (Modbus 10.10.1.15:502 · Cnl 141–142)",
        "VF-101" or "GRIZ-102" or "PAN-101" or "DRV-101" => "Direct PLC: ROM101 (Modbus 10.10.1.15:502 · Cnl 143)",
        "JC-101" or "FW-101" or "JAW-101" or "M-JC101" or "CHUTE-103" => "Direct PLC: CR101 (Modbus 10.10.1.11:502 · Cnl 101–103)",
        "DRC-101" or "ROLL-101" or "HOP-101" or "M-DRC101A" or "M-DRC101B" or "CHUTE-104" => "Direct PLC: DRC101 (Modbus 10.10.1.16:502 · Cnl 151–153)",
        "SS-101" or "FBOX-101" or "DECK-101A" or "DECK-101B" or "DRV-102" or "HOP-102" or "VT-102" or "CHUTE-105" => "Direct PLC: SC102 (Modbus 10.10.1.12:502 · Cnl 111–112)",
        "CV-101" or "CHUTE-101" or "BW-101" or "M-CV101" or "RC-101" => "Direct PLC: CV101 (Modbus 10.10.1.13:502 · Cnl 121–122, 711)",
        "CV-102" or "CHUTE-102" or "BW-102" or "M-CV102" or "RC-102" => "Direct PLC: CV102 (Modbus 10.10.1.14:502 · Cnl 131–132, 712)",
        "CV-103" or "DIV-101" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 161)",
        "CV-104" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 162)",
        "CV-105" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 163)",
        "CV-106" or "CHUTE-106" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 164)",
        "CV-201" or "BW-201" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 165)",
        "BN-201" or "GATE-201" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 166)",
        "CV-202" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 311)",
        "DS-201" or "PAN-201" or "PIPE-310" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 312–313)",
        "CV-203" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 314)",
        "CV-301" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 315)",
        "CV-302" or "PIPE-313" or "VALVE-302" or "APEX-301" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 316)",
        "TK-201" or "RAKE-201" or "LAUNDER-201" or "LT-201" => "Direct PLC: TK201 (Modbus 10.10.2.21:502 · Cnl 201–203)",
        "PP-201" or "VALVE-201" => "Direct PLC: PP201 (Modbus 10.10.2.22:502 · Cnl 211–212)",
        "PP-202" or "VALVE-202" or "PIPE-311" => "Direct PLC: PP202 (Modbus 10.10.2.23:502 · Cnl 221)",
        "PP-203" or "SPRAY-201" or "VALVE-204" or "VALVE-203" or "VALVE-303" or "VALVE-305" or "PIPE-308" or "PIPE-309" => "Direct PLC: PP203 (Modbus 10.10.2.24:502 · Cnl 231)",
        "TK-301" or "MS-301" or "WP-301" or "AGIT-302" or "DT-301" => "Direct PLC: TK301/DMC (Modbus 10.10.3.31:502 · Cnl 301–305)",
        "MX-301" or "PP-301" or "DMC-301" or "AGIT-301" or "PIPE-312" or "VALVE-301" or "PT-301" or "VF-301" => "Direct PLC: TK301/DMC (Modbus 10.10.3.31:502 · Cnl 304–305)",
        "SC-301" or "SC-302" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 315–316)",
        "SPRAY-301" => "Direct PLC: PP203 (Modbus 10.10.2.24:502 · Cnl 231)",
        "BK-301" or "PP-302" or "PIPE-301" or "PIPE-302" => "Direct PLC: TK301/DMC (Modbus 10.10.3.31:502 · Cnl 303)",
        "PIPE-303" or "PIPE-304" => "Direct PLC: TK301/DMC (Modbus 10.10.3.31:502 · Cnl 301–302)",
        "PIPE-305" => "Direct PLC: TK201 (Modbus 10.10.2.21:502 · Cnl 201–203)",
        "PIPE-306" => "Direct PLC: PP201 (Modbus 10.10.2.22:502 · Cnl 211–212)",
        "PIPE-307" => "Direct PLC: PP203 (Modbus 10.10.2.24:502 · Cnl 231)",
        "OM-101" => "Direct PLC: CV101 (Modbus 10.10.1.13:502 · Cnl 121–122)",
        "DC-101" => "Direct PLC: SC102 (Modbus 10.10.1.12:502 · Cnl 111–112)",
        "CF-501" => "Direct PLC: CF501 (Modbus 10.10.5.51:502 · Cnl 501–503)",
        "AN-601" => "Direct PLC: AN601 (Modbus 10.10.6.61:502 · Cnl 601–603)",
        "FP-501" => "Direct PLC: FP501 (Modbus 10.10.5.52:502 · Cnl 511–512)",
        "TLS-401" => "Direct PLC: LD601 (Modbus 10.10.6.62:502 · Cnl 611–612)",
        "MCC-101" => "Direct PLC: CR101 (Modbus 10.10.1.11:502 · Cnl 101–103)",
        _ => $"Derived Process Model (coupled to {linkedScadaId})"
    };

    /// <summary>
    /// Formats provisional/confirmed engineering configuration parameters for the given 3D equipment or conveyor node.
    /// </summary>
    public static string FormatProvisionalConfigNote(Plant3DNode node, ChppEngineeringConfig cfg)
    {
        string status = cfg.IsSiteConfirmed
            ? "[SITE CONFIRMED]"
            : "[PROVISIONAL — Awaiting Site Confirmation]";

        var parts = new List<string>(4);
        if (cfg.GetRatedMotorKw(node.Id) is double motorKw)
            parts.Add($"Motor: {motorKw:0.#} kW");

        if (node.IsConveyor)
        {
            int beltWidth = node.Id.ToUpperInvariant() switch
            {
                "CV-101" or "CV-103" or "CV-104" or "CV-201" or "CV-202" or "CV-203" => cfg.PrimaryBeltWidthMm,
                _ => cfg.SecondaryBeltWidthMm
            };
            parts.Add($"Belt Width: {beltWidth} mm");
        }

        switch (node.Id.ToUpperInvariant())
        {
            case "VF-101":
                parts.Add($"Grizzly Aperture: {cfg.GrizzlyApertureMm:0.#} mm");
                break;
            case "SS-101":
                parts.Add($"Decks: {cfg.SizingTopDeckApertureMm:0.#} / {cfg.SizingBottomDeckApertureMm:0.#} mm");
                parts.Add($"Fines Dest: {cfg.DryFinesDestinationMode}");
                break;
            case "DS-201":
                parts.Add($"Desliming Aperture: {cfg.DeslimingApertureMm:0.#} mm");
                break;
            case "JC-101":
            case "DRC-101":
            case "CV-103":
            case "CV-104":
                parts.Add($"Jaw Routing: {cfg.JawDischargeRoutingMode}");
                break;
            case "CV-105":
                parts.Add($"Fines Dest: {cfg.DryFinesDestinationMode}");
                break;
        }

        if (parts.Count == 0)
        {
            parts.Add($"Grizzly {cfg.GrizzlyApertureMm:0.#} mm");
            parts.Add($"Sizing {cfg.SizingTopDeckApertureMm:0.#}/{cfg.SizingBottomDeckApertureMm:0.#} mm");
            parts.Add($"Jaw Routing: {cfg.JawDischargeRoutingMode}");
        }

        return $"{status} {string.Join(" · ", parts)}";
    }

    /// <summary>
    /// Evaluates upstream-to-downstream coupled material flow across all 29 plant nodes.
    /// If an upstream feeder/crusher/conveyor stops or faults, downstream feed rates drop proportionally.
    /// </summary>
    public static IReadOnlyDictionary<string, ProcessFlowState> EvaluateProcessFlows(
        IReadOnlyDictionary<string, EquipmentItem> eqMap,
        double simTime,
        ChppEngineeringConfig? config = null,
        IReadOnlySet<string>? localNodeFaultOverrides = null)
    {
        eqMap ??= new Dictionary<string, EquipmentItem>(StringComparer.OrdinalIgnoreCase);
        if (!double.IsFinite(simTime))
            simTime = 0.0;
        var cfg = config ?? ChppEngineeringConfig.Current;
        var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var localSpeeds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in AllNodes)
        {
            double local;
            if (localNodeFaultOverrides is not null && localNodeFaultOverrides.Contains(node.Id))
            {
                local = 0.0;
            }
            else
            {
                eqMap.TryGetValue(node.LinkedScadaId, out var eq);
                local = GetFlowSpeedFactor(eq);
            }
            localSpeeds[node.Id] = local;
            rates[node.Id] = local;
        }

        var loaderPose = ComputeLoaderPose(simTime, localSpeeds.GetValueOrDefault("FEL-101") > 0);
        double binSurgeWave = 0.92 + 0.16 * loaderPose.DumpIntensity;

        // Iterative fixed-point propagation handles converging streams, recirculation (SS-101 -> CV-106 -> DRC-101),
        // and multi-hop downstream Wash Plant / Stockpile starvation cleanly.
        for (int pass = 0; pass < 16; pass++)
        {
            double maxDelta = 0.0;
            foreach (var node in AllNodes)
            {
                double local = localSpeeds[node.Id];
                if (local <= 0.0)
                {
                    if (rates[node.Id] != 0.0)
                    {
                        maxDelta = Math.Max(maxDelta, Math.Abs(rates[node.Id]));
                        rates[node.Id] = 0.0;
                    }
                    continue;
                }

                if (node.UpstreamIds.Length == 0)
                {
                    if (rates[node.Id] != local)
                    {
                        maxDelta = Math.Max(maxDelta, Math.Abs(rates[node.Id] - local));
                        rates[node.Id] = local;
                    }
                    continue;
                }

                double upstreamFactor;
                if (string.Equals(node.Id, "SS-101", StringComparison.OrdinalIgnoreCase))
                {
                    // Dual convergence: Path B bypass (CV-102) + Crushed stream (CV-104)
                    upstreamFactor = 0.45 * rates.GetValueOrDefault("CV-102") + 0.55 * rates.GetValueOrDefault("CV-104");
                }
                else if (string.Equals(node.Id, "DRC-101", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(cfg.JawDischargeRoutingMode, ChppEngineeringConfig.RoutingBypassToCv104, StringComparison.OrdinalIgnoreCase))
                    {
                        // Jaw discharge CV-103 bypasses DRC-101 directly onto CV-104; DRC-101 crushes SS-101 oversize (CV-106)
                        upstreamFactor = rates.GetValueOrDefault("CV-106");
                    }
                    else if (string.Equals(cfg.JawDischargeRoutingMode, ChppEngineeringConfig.RoutingSplitDiverter50_50, StringComparison.OrdinalIgnoreCase))
                    {
                        upstreamFactor = 0.50 * rates.GetValueOrDefault("CV-103") + 0.50 * rates.GetValueOrDefault("CV-106");
                    }
                    else
                    {
                        // DivertThroughDoubleRollCrusher: Primary stream (CV-103) + Recirculated oversize (CV-106)
                        upstreamFactor = 0.72 * rates.GetValueOrDefault("CV-103") + 0.28 * rates.GetValueOrDefault("CV-106");
                    }
                }
                else if (string.Equals(node.Id, "CV-104", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(cfg.JawDischargeRoutingMode, ChppEngineeringConfig.RoutingBypassToCv104, StringComparison.OrdinalIgnoreCase))
                    {
                        upstreamFactor = 0.72 * rates.GetValueOrDefault("CV-103") + 0.28 * rates.GetValueOrDefault("DRC-101");
                    }
                    else if (string.Equals(cfg.JawDischargeRoutingMode, ChppEngineeringConfig.RoutingSplitDiverter50_50, StringComparison.OrdinalIgnoreCase))
                    {
                        upstreamFactor = 0.36 * rates.GetValueOrDefault("CV-103") + 0.64 * rates.GetValueOrDefault("DRC-101");
                    }
                    else
                    {
                        upstreamFactor = rates.GetValueOrDefault("DRC-101");
                    }
                }
                else if (string.Equals(node.Id, "WP-301", StringComparison.OrdinalIgnoreCase))
                {
                    double coalIn = rates.GetValueOrDefault("CV-203");
                    double mediumIn = rates.GetValueOrDefault("TK-301");
                    upstreamFactor = coalIn * (mediumIn > 0.05 ? 1.0 : 0.25);
                }
                else if (string.Equals(node.Id, "BN-101", StringComparison.OrdinalIgnoreCase))
                {
                    upstreamFactor = rates.GetValueOrDefault("FEL-101") * binSurgeWave;
                }
                else
                {
                    double maxUp = 0.0;
                    foreach (var up in node.UpstreamIds)
                        maxUp = Math.Max(maxUp, rates.GetValueOrDefault(up));
                    upstreamFactor = maxUp;
                }

                double nextRate = Math.Clamp(local * Math.Clamp(upstreamFactor, 0.0, 1.25), 0.0, 1.35);
                maxDelta = Math.Max(maxDelta, Math.Abs(nextRate - rates[node.Id]));
                rates[node.Id] = nextRate;
            }

            if (maxDelta < 1e-4)
                break;
        }

        var result = new Dictionary<string, ProcessFlowState>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in AllNodes)
        {
            double local = localSpeeds[node.Id];
            double eff = rates[node.Id];
            bool starved = local > 0.0 && node.UpstreamIds.Length > 0 && eff < 0.05;
            double nomTph = GetNominalTph(node.Id);
            result[node.Id] = new ProcessFlowState(
                NodeId: node.Id,
                NormalizedRate: eff,
                IsRunning: local > 0.0,
                IsStarved: starved,
                EstimatedTph: Math.Round(nomTph * eff, 1));
        }

        return result;
    }

    /// <summary>
    /// Benchmarks cold GLB loading, GPU VBO footprint, managed memory delta, and 1,000-iteration flow/pose evaluation.
    /// </summary>
    public static Plant3DPerformanceMetrics BenchmarkScenePerformance()
    {
        InvalidateMeshCache();
        long memBefore = GC.GetTotalMemory(forceFullCollection: true);

        var loadSw = Stopwatch.StartNew();
        var meshes = LoadAllGlbMeshes();
        loadSw.Stop();

        long memAfter = GC.GetTotalMemory(forceFullCollection: false);
        long memDelta = Math.Max(0, memAfter - memBefore);

        var exportDir = FindAssetsExportDirectory();
        var meshMetrics = new List<Plant3DGlbMeshMetric>(meshes.Count);
        int totalVerts = 0;
        int totalTris = 0;
        long totalVboBytes = 0;

        foreach (var (fileName, mesh) in meshes.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            long fileBytes = 0;
            if (exportDir is not null)
            {
                var fullPath = Path.Combine(exportDir, fileName);
                if (File.Exists(fullPath))
                    fileBytes = new FileInfo(fullPath).Length;
            }

            long vboBytes = (long)mesh.Vertices.Length * sizeof(float) + (long)mesh.Indices.Length * sizeof(uint);
            totalVerts += mesh.VertexCount;
            totalTris += mesh.TriangleCount;
            totalVboBytes += vboBytes;

            meshMetrics.Add(new Plant3DGlbMeshMetric(
                FileName: fileName,
                FileSizeBytes: fileBytes,
                VertexCount: mesh.VertexCount,
                TriangleCount: mesh.TriangleCount,
                VboBytes: vboBytes));
        }

        var eqMap = new Dictionary<string, EquipmentItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in AllNodes.Select(n => n.LinkedScadaId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            eqMap[id] = new EquipmentItem
            {
                Id = id,
                Status = EquipmentStatus.Running,
                LoadPercent = 75.0
            };
        }

        // Warmup JIT
        _ = EvaluateProcessFlows(eqMap, 4.8);
        _ = ComputeLoaderPose(4.8, active: true);

        const int iterations = 1000;
        var flowSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            double t = i * 0.033;
            _ = EvaluateProcessFlows(eqMap, t);
            _ = ComputeLoaderPose(t, active: true);
        }
        flowSw.Stop();

        return new Plant3DPerformanceMetrics(
            Meshes: meshMetrics,
            TotalGlbCount: meshes.Count,
            TotalVertices: totalVerts,
            TotalTriangles: totalTris,
            TotalGpuVboMiB: Math.Round(totalVboBytes / (1024.0 * 1024.0), 3),
            ColdGlbLoadMs: Math.Round(loadSw.Elapsed.TotalMilliseconds, 2),
            FlowAndPoseMicrosPerFrame: Math.Round(flowSw.Elapsed.TotalMicroseconds / iterations, 2),
            ManagedMemoryDeltaBytes: memDelta);
    }

    private static double GetNominalTph(string id) => id switch
    {
        "SP-001" or "FEL-101" or "BN-101" or "VF-101" => 650.0,
        "CV-101" or "JC-101" or "CV-103" => 420.0,
        "CV-102" => 230.0,
        "CV-106" => 110.0,
        "DRC-101" or "CV-104" => 530.0,
        "SS-101" => 760.0,
        "CV-105" => 140.0,
        "CV-201" or "BN-201" or "CV-202" or "DS-201" or "CV-203" or "WP-301" => 510.0,
        "CV-301" or "SP-301" => 410.0,
        "CV-302" or "SP-302" => 100.0,
        _ => 320.0
    };

    public static Color GetStatusTint(Plant3DNode node, EquipmentItem? eq, Unity3DEffectSettings fx)
    {
        if (eq is null) return node.DefaultColor;
        if (fx.ShowThermalHeatmap)
        {
            if (eq.LoadPercent >= 85) return Color.Parse("#EF4444");
            if (eq.LoadPercent >= 70) return Color.Parse("#F59E0B");
            if (eq.LoadPercent >= 40) return Color.Parse("#22C55E");
            return Color.Parse("#38BDF8");
        }

        return eq.Status switch
        {
            EquipmentStatus.Warning => Color.Parse("#F59E0B"),
            EquipmentStatus.Fault => Color.Parse("#EF4444"),
            EquipmentStatus.Stopped => Color.Parse("#475569"),
            _ => node.DefaultColor
        };
    }

    public static Color GetMediumColor(FlowMedium medium) => medium switch
    {
        FlowMedium.Coal => Color.Parse("#181C22"),
        FlowMedium.Froth => Color.Parse("#22C55E"),
        FlowMedium.Magnetite => Color.Parse("#14B8A6"),
        _ => Color.Parse("#38BDF8")
    };

    public static Color GetMaterialColor(CoalMaterialClass mat) => mat switch
    {
        CoalMaterialClass.RawRom => Color.Parse("#1C1E22"),
        CoalMaterialClass.ScalpedUndersize => Color.Parse("#181B20"),
        CoalMaterialClass.CrushedPrimary => Color.Parse("#16191E"),
        CoalMaterialClass.ScreenOversize => Color.Parse("#1A1D22"),
        CoalMaterialClass.CrushedRoll => Color.Parse("#14171C"),
        CoalMaterialClass.SizedFines => Color.Parse("#0E1116"),
        CoalMaterialClass.PlantFeed => Color.Parse("#12151A"),
        CoalMaterialClass.DeslimedCoal => Color.Parse("#0D1015"),
        CoalMaterialClass.CleanProduct => Color.Parse("#0A0D12"),
        CoalMaterialClass.RefuseDiscard => Color.Parse("#23272F"),
        _ => Color.Parse("#14171C")
    };

    public static float GetMaterialLumpSize(CoalMaterialClass mat) => mat switch
    {
        CoalMaterialClass.RawRom => 0.34f,
        CoalMaterialClass.CrushedPrimary => 0.22f,
        CoalMaterialClass.ScreenOversize => 0.20f,
        CoalMaterialClass.ScalpedUndersize => 0.175f,
        CoalMaterialClass.CrushedRoll => 0.135f,
        CoalMaterialClass.PlantFeed => 0.105f,
        CoalMaterialClass.DeslimedCoal => 0.095f,
        CoalMaterialClass.RefuseDiscard => 0.090f,
        CoalMaterialClass.CleanProduct => 0.080f,
        CoalMaterialClass.SizedFines => 0.055f,
        _ => 0.12f
    };

    public static int GetMaterialSubClusterCount(CoalMaterialClass mat) => mat switch
    {
        CoalMaterialClass.RawRom => 1,
        CoalMaterialClass.CrushedPrimary or CoalMaterialClass.ScreenOversize or CoalMaterialClass.ScalpedUndersize => 2,
        CoalMaterialClass.CrushedRoll or CoalMaterialClass.PlantFeed or CoalMaterialClass.DeslimedCoal or CoalMaterialClass.RefuseDiscard or CoalMaterialClass.CleanProduct => 3,
        CoalMaterialClass.SizedFines => 4,
        _ => 2
    };

    /// <summary>
    /// Computes normalized flow speed (0 when Stopped/Fault, otherwise scaled by LoadPercent).
    /// </summary>
    public static double GetFlowSpeedFactor(EquipmentItem? source)
    {
        if (source is null) return 0.65;
        if (source.Status is EquipmentStatus.Stopped or EquipmentStatus.Fault or EquipmentStatus.Offline)
            return 0.0;
        if (double.IsNaN(source.LoadPercent) || double.IsNegativeInfinity(source.LoadPercent))
            return 0.35;
        if (double.IsPositiveInfinity(source.LoadPercent))
            return 1.30;
        return Math.Clamp(0.35 + (source.LoadPercent / 100.0) * 0.95, 0.35, 1.30);
    }

    /// <summary>
    /// Samples a point at normalized parameter t in [0,1] along a multi-waypoint polyline.
    /// </summary>
    public static Vector3 SamplePolyline(Vector3[] pts, float t)
    {
        if (pts is null || pts.Length == 0) return Vector3.Zero;
        if (pts.Length == 1) return pts[0];

        float total = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
            total += Vector3.Distance(pts[i], pts[i + 1]);
        if (!float.IsFinite(total) || total < 1e-5f) return pts[0];

        float tc = float.IsNaN(t) ? 0f : Math.Clamp(t, 0f, 1f);
        float target = tc * total;
        float accum = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            float seg = Vector3.Distance(pts[i], pts[i + 1]);
            if (accum + seg >= target || i == pts.Length - 2)
            {
                float local = seg > 1e-5f ? (target - accum) / seg : 0f;
                return Vector3.Lerp(pts[i], pts[i + 1], Math.Clamp(local, 0f, 1f));
            }
            accum += seg;
        }
        return pts[^1];
    }

    /// <summary>
    /// Returns true for washed/deslimed wet coal streams that exhibit water-film clearcoat sheen.
    /// </summary>
    public static bool IsWetCoalStream(CoalMaterialClass mat) =>
        mat is CoalMaterialClass.DeslimedCoal or CoalMaterialClass.CleanProduct or CoalMaterialClass.RefuseDiscard;

    /// <summary>
    /// Returns physically calibrated (Metallic, Roughness, Clearcoat) parameters for each CHPP coal stream.
    /// Wet streams carry a thin water-film clearcoat (low base roughness + high clearcoat weight);
    /// dry crushed streams exhibit matte dusty roughness.
    /// </summary>
    public static (float Metallic, float Roughness, float Clearcoat) GetMaterialPbrParams(CoalMaterialClass mat)
    {
        if (IsWetCoalStream(mat))
            return (Metallic: 0.08f, Roughness: 0.34f, Clearcoat: 0.85f);

        return mat switch
        {
            CoalMaterialClass.CrushedRoll or CoalMaterialClass.PlantFeed => (0.06f, 0.72f, 0.0f),
            _ => (0.05f, 0.86f, 0.0f)
        };
    }

    /// <summary>
    /// Computes normalized continuous troughed coal burden ribbon thickness [0..0.16] m for an active belt.
    /// </summary>
    public static float ComputeBeltBurdenThickness(CoalMaterialClass mat, double normalizedFlowRate)
    {
        if (!double.IsFinite(normalizedFlowRate) || normalizedFlowRate <= 0.02)
            return 0f;

        float lumpScale = mat switch
        {
            CoalMaterialClass.RawRom => 1.28f,
            CoalMaterialClass.CrushedPrimary or CoalMaterialClass.ScreenOversize => 1.12f,
            CoalMaterialClass.ScalpedUndersize or CoalMaterialClass.CrushedRoll => 1.00f,
            CoalMaterialClass.SizedFines => 0.76f,
            _ => 0.90f
        };
        return Math.Clamp((float)normalizedFlowRate * 0.14f * lumpScale, 0.025f, 0.17f);
    }

    /// <summary>
    /// Computes 3-axis elliptical vibrating screen/feeder displacement driven by machine type and load.
    /// </summary>
    public static Vector3 ComputeScreenVibrationOffset(string nodeId, double simTime, double loadPercent)
    {
        if (!double.IsFinite(simTime) || !double.IsFinite(loadPercent))
            return Vector3.Zero;

        float load = Math.Clamp((float)(loadPercent / 100.0), 0.15f, 1.15f);
        float t = (float)simTime;

        return nodeId.ToUpperInvariant() switch
        {
            // Linear grizzly feeder: 45 deg throw along Z/Y at 31 rad/s
            "VF-101" => new Vector3(
                MathF.Sin(t * 15.5f) * 0.004f * load,
                MathF.Sin(t * 31.0f) * (0.015f + 0.018f * load),
                -MathF.Cos(t * 31.0f) * (0.014f + 0.014f * load)),
            // Desliming screen: high-frequency dewatering elliptical throw in (-X, Y)
            "DS-201" => new Vector3(
                -MathF.Cos(t * 34.0f) * (0.012f + 0.015f * load),
                MathF.Sin(t * 34.0f) * (0.016f + 0.022f * load),
                MathF.Sin(t * 17.0f) * 0.004f * load),
            // Central multi-deck sizing screen: dual-shaft circular/elliptical motion
            _ => new Vector3(
                MathF.Cos(t * 28.0f) * (0.014f + 0.016f * load),
                MathF.Sin(t * 28.0f) * (0.018f + 0.024f * load),
                MathF.Sin(t * 14.0f) * 0.006f * load)
        };
    }

    /// <summary>
    /// Emits fugitive coal dust plumes at dry comminution transfer points (BN-101, JC-101, DRC-101)
    /// and pressurized desliming water spray curtains (DS-201, WP-301) for soft-particle rendering.
    /// </summary>
    public static List<SoftParticleInstance> ComputeDustAndSprayParticles(
        IReadOnlyDictionary<string, ProcessFlowState> flowMap,
        LoaderPose loaderPose,
        double simTime)
    {
        var list = new List<SoftParticleInstance>(64);
        if (!double.IsFinite(simTime))
            return list;

        float t = (float)simTime;

        void EmitPlume(string nodeId, Vector3 origin, float spreadX, float riseH, float baseScale, bool isWater, int count)
        {
            if (!flowMap.TryGetValue(nodeId, out var flow) || !flow.IsRunning || flow.NormalizedRate <= 0.04)
                return;

            float intensity = Math.Clamp((float)flow.NormalizedRate, 0.25f, 1.25f);
            for (int i = 0; i < count; i++)
            {
                float age = (t * (isWater ? 1.55f : 0.42f) + i / (float)count) % 1.0f;
                float angle = i * 2.39996f + t * (isWater ? 0.6f : 0.35f);
                float r = spreadX * (0.25f + age * 0.85f);
                Vector3 pos = isWater
                    ? new Vector3(
                        origin.X + MathF.Cos(angle) * r,
                        origin.Y - age * riseH + MathF.Sin(age * MathF.PI) * 0.06f,
                        origin.Z + MathF.Sin(angle) * r * 1.35f)
                    : new Vector3(
                        origin.X + MathF.Cos(angle) * r,
                        origin.Y + age * riseH,
                        origin.Z + MathF.Sin(angle) * r);

                float s = baseScale * (isWater ? (0.45f + age * 0.65f) : (0.55f + age * 1.45f));
                float alpha = MathF.Sin(age * MathF.PI) * (isWater ? 0.38f : 0.30f) * MathF.Min(intensity, 1.0f);
                if (alpha <= 0.01f) continue;

                if (isWater)
                {
                    list.Add(new SoftParticleInstance(pos, new Vector3(s * 0.7f, s * 1.35f, s * 0.7f), 0.72f, 0.90f, 1.00f, alpha, true));
                }
                else
                {
                    list.Add(new SoftParticleInstance(pos, new Vector3(s, s * 0.72f, s), 0.14f, 0.16f, 0.19f, alpha, false));
                }
            }
        }

        // Fugitive coal dust plumes at dry comminution transfer/crushing throats
        if (loaderPose.IsDumpingCoal || (flowMap.TryGetValue("BN-101", out var bnFlow) && bnFlow.IsRunning && bnFlow.NormalizedRate > 0.05))
        {
            EmitPlume("BN-101", new Vector3(-13.8f, 6.70f, 8.8f), 0.85f, 1.85f, 0.48f, isWater: false, count: 10);
        }
        EmitPlume("JC-101", new Vector3(-13.8f, 3.15f, -0.6f), 0.72f, 1.95f, 0.44f, isWater: false, count: 10);
        EmitPlume("DRC-101", new Vector3(-7.2f, 2.85f, -0.6f), 0.68f, 1.75f, 0.42f, isWater: false, count: 9);
        EmitPlume("SS-101", new Vector3(0.2f, 3.30f, 0.5f), 0.95f, 1.45f, 0.40f, isWater: false, count: 8);

        // Pressurized desliming & dense-medium magnetite-reclaim water spray curtains over Product & Discard wash screens
        EmitPlume("DS-201", new Vector3(8.8f, 2.75f, -9.8f), 0.78f, 1.05f, 0.22f, isWater: true, count: 14);
        EmitPlume("WP-301", new Vector3(-2.3f, 3.05f, -6.8f), 0.95f, 0.75f, 0.20f, isWater: true, count: 10);
        EmitPlume("WP-301", new Vector3(-2.3f, 3.05f, -8.4f), 0.95f, 0.75f, 0.20f, isWater: true, count: 10);

        return list;
    }

    /// <summary>
    /// Samples a coal parcel along a conveyor: linear belt travel from TailPos to HeadPos for u in [0, 0.82],
    /// followed by a Newtonian parabolic trajectory with inelastic chute wall bounce (e = 0.18) and
    /// kinetic friction slide (mu_k = 0.22 wet / 0.38 dry) into the enclosed transfer chute for u in (0.82, 1.0].
    /// </summary>
    public static Vector3 SampleConveyorCoalTrajectory(ConveyorSpec conv, float u)
    {
        ArgumentNullException.ThrowIfNull(conv);
        float uc = float.IsNaN(u) ? 0f : Math.Clamp(u, 0f, 1f);
        const float beltEndU = 0.82f;
        if (uc <= beltEndU)
        {
            return Vector3.Lerp(conv.TailPos, conv.HeadPos, uc / beltEndU);
        }

        float tauNorm = (uc - beltEndU) / (1f - beltEndU);
        var chuteBot = conv.GetChuteBottomPos();
        var delta = conv.HeadPos - conv.TailPos;
        float beltLen = delta.Length();
        var beltDir = beltLen > 1e-4f ? delta / beltLen : Vector3.UnitX;

        const float beltSpeed = 2.6f;
        const float gravity = 9.81f;
        var vBelt = beltDir * beltSpeed;
        float v0y = MathF.Max(0.25f, vBelt.Y * 0.45f);
        float dropH = MathF.Max(0f, conv.HeadPos.Y - chuteBot.Y);
        float fallTime = (v0y + MathF.Sqrt(v0y * v0y + 2f * gravity * dropH)) / gravity;
        if (!float.IsFinite(fallTime) || fallTime < 0f) fallTime = 0f;
        float t = tauNorm * fallTime;

        float y = conv.HeadPos.Y + v0y * t - 0.5f * gravity * t * t;

        float freeX = conv.HeadPos.X + vBelt.X * t;
        float freeZ = conv.HeadPos.Z + vBelt.Z * t;
        float cx = conv.HeadPos.X + (chuteBot.X - conv.HeadPos.X) * tauNorm;
        float cz = conv.HeadPos.Z + (chuteBot.Z - conv.HeadPos.Z) * tauNorm;

        // Inelastic chute-hood impact at tauNorm = 0.58 with restitution e = 0.18 and sliding friction
        const float impactTau = 0.58f;
        const float restitution = 0.18f;
        float muKinetic = IsWetCoalStream(conv.MaterialClass) ? 0.22f : 0.38f;

        float dx = freeX - cx;
        float dz = freeZ - cz;
        if (tauNorm > impactTau)
        {
            float postS = (tauNorm - impactTau) / (1f - impactTau);
            float bounceSign = MathF.Sin(postS * MathF.PI) * restitution;
            float frictionDamp = MathF.Exp(-muKinetic * 4.5f * postS);
            float factor = frictionDamp * (1f - postS) * (1f - bounceSign);
            dx *= factor;
            dz *= factor;
        }

        float dev = MathF.Sqrt(dx * dx + dz * dz);
        float maxDev = MathF.Min(0.26f, conv.Width * 0.30f) * (1f - tauNorm);
        if (dev > maxDev && dev > 1e-5f)
        {
            float scale = maxDev / dev;
            dx *= scale;
            dz *= scale;
        }

        return new Vector3(cx + dx, y, cz + dz);
    }

    /// <summary>
    /// Builds standard right-handed View &amp; Projection matrices matching the orbit camera state.
    /// </summary>
    public static (Matrix4x4 View, Matrix4x4 Proj, Vector3 CameraPos) BuildCameraMatrices(
        double yaw, double pitch, double zoom, double targetX, double targetZ,
        double width, double height, bool isOrthographic, double targetY = 0.9)
    {
        float dist = (float)(19.0 / Math.Clamp(zoom, 0.35, 3.2));
        var target = new Vector3((float)targetX, (float)targetY, (float)targetZ);

        float cy = (float)Math.Cos(yaw);
        float sy = (float)Math.Sin(yaw);
        float cp = (float)Math.Cos(pitch);
        float sp = (float)Math.Sin(pitch);

        var offset = new Vector3(
            dist * cp * sy,
            dist * sp,
            dist * cp * cy);

        var camPos = target + offset;
        var view = Matrix4x4.CreateLookAt(camPos, target, Vector3.UnitY);

        float aspect = (float)Math.Max(0.1, width / Math.Max(1.0, height));
        Matrix4x4 proj;
        if (isOrthographic)
        {
            float orthoHalfH = (float)(9.5 / Math.Clamp(zoom, 0.35, 3.2));
            proj = CreateOpenGlOrthographic(orthoHalfH * 2f * aspect, orthoHalfH * 2f, 0.1f, 140f);
        }
        else
        {
            proj = CreateOpenGlPerspective(MathF.PI / 4.2f, aspect, 0.2f, 140f);
        }

        return (view, proj, camPos);
    }

    public static Matrix4x4 CreateOpenGlPerspective(float fovY, float aspect, float near, float far)
    {
        float yScale = 1.0f / MathF.Tan(fovY * 0.5f);
        float xScale = yScale / aspect;
        float invRange = 1.0f / (near - far);

        return new Matrix4x4(
            xScale, 0f, 0f, 0f,
            0f, yScale, 0f, 0f,
            0f, 0f, (near + far) * invRange, -1f,
            0f, 0f, 2f * near * far * invRange, 0f);
    }

    public static Matrix4x4 CreateOpenGlOrthographic(float width, float height, float near, float far)
    {
        float invRange = 1.0f / (near - far);

        return new Matrix4x4(
            2f / width, 0f, 0f, 0f,
            0f, 2f / height, 0f, 0f,
            0f, 0f, 2f * invRange, 0f,
            0f, 0f, (near + far) * invRange, 1f);
    }

    public static bool TryProjectToScreen(Vector3 world, Matrix4x4 viewProj, double width, double height, out Point screen, out float depth)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 0.05f)
        {
            screen = default;
            depth = float.PositiveInfinity;
            return false;
        }

        float invW = 1f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;
        float ndcZ = clip.Z * invW;

        screen = new Point((ndcX * 0.5f + 0.5f) * width, (0.5f - ndcY * 0.5f) * height);
        depth = ndcZ;
        return ndcZ >= -1.0f && ndcZ <= 1.0f;
    }

    public static Ray3D UnprojectRay(Point screenPos, Matrix4x4 viewProj, double width, double height)
    {
        float ndcX = (float)((screenPos.X / Math.Max(1.0, width)) * 2.0 - 1.0);
        float ndcY = (float)(1.0 - (screenPos.Y / Math.Max(1.0, height)) * 2.0);

        if (!Matrix4x4.Invert(viewProj, out var invVp))
            return new Ray3D(Vector3.Zero, -Vector3.UnitZ);

        var nearClip = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVp);
        var farClip = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invVp);

        var nearPos = new Vector3(nearClip.X, nearClip.Y, nearClip.Z) / Math.Max(1e-6f, nearClip.W);
        var farPos = new Vector3(farClip.X, farClip.Y, farClip.Z) / Math.Max(1e-6f, farClip.W);

        return new Ray3D(nearPos, Vector3.Normalize(farPos - nearPos));
    }

    /// <summary>
    /// Performs exact 3D picking across all 18 plant machines (Ray-AABB) and 11 conveyors (Ray-Capsule along belt centerline).
    /// </summary>
    public static string? PickEquipment(Ray3D ray, IReadOnlyDictionary<string, GlbMeshData>? meshes = null, Vector3? dynamicLoaderPos = null)
    {
        string? bestId = null;
        float bestT = float.PositiveInfinity;

        foreach (var p in MachineNodes)
        {
            var pos = (p.Id == "FEL-101" && dynamicLoaderPos.HasValue) ? dynamicLoaderPos.Value : p.Position;
            Vector3 min, max;
            if (meshes is not null && meshes.TryGetValue(p.GlbFileName, out var mesh))
            {
                var scaledMin = mesh.BoundsMin * p.Scale;
                var scaledMax = mesh.BoundsMax * p.Scale;
                float rx = MathF.Max(MathF.Abs(scaledMin.X), MathF.Abs(scaledMax.X));
                float rz = MathF.Max(MathF.Abs(scaledMin.Z), MathF.Abs(scaledMax.Z));
                bool rot90 = MathF.Abs(p.YawDegrees % 180f) > 45f && MathF.Abs(p.YawDegrees % 180f) < 135f;
                float hx = rot90 ? rz : rx;
                float hz = rot90 ? rx : rz;
                min = new Vector3(pos.X - hx, pos.Y + scaledMin.Y, pos.Z - hz);
                max = new Vector3(pos.X + hx, pos.Y + scaledMax.Y, pos.Z + hz);
            }
            else
            {
                min = new Vector3(pos.X - p.HalfExtents.X, pos.Y, pos.Z - p.HalfExtents.Z);
                max = new Vector3(pos.X + p.HalfExtents.X, pos.Y + p.HalfExtents.Y * 2f, pos.Z + p.HalfExtents.Z);
            }

            if (RayIntersectsAabb(ray, min, max, out float t) && t < bestT)
            {
                bestT = t;
                bestId = p.Id;
            }
        }

        // Oriented capsule picking along every conveyor centerline so diagonal belts never occlude machines below
        foreach (var c in Conveyors)
        {
            float rad = c.Width * 0.62f + 0.18f;
            if (RayIntersectsSegmentCapsule(ray, c.TailPos, c.HeadPos, rad, out float t) && t < bestT)
            {
                bestT = t;
                bestId = c.Id;
            }
        }

        // Individually selectable wash-plant sub-equipment & process pipes take priority over the
        // enclosing WP-301 structural frame (whose AABB wraps them).
        string? fineId = null;
        float fineT = float.PositiveInfinity;
        foreach (var s in SubEquipmentNodes)
        {
            var min = new Vector3(s.Position.X - s.HalfExtents.X, s.Position.Y, s.Position.Z - s.HalfExtents.Z);
            var max = new Vector3(s.Position.X + s.HalfExtents.X, s.Position.Y + s.HalfExtents.Y * 2f, s.Position.Z + s.HalfExtents.Z);
            if (RayIntersectsAabb(ray, min, max, out float t) && t < fineT)
            {
                fineT = t;
                fineId = s.Id;
            }
        }
        foreach (var r in PipeRoutes)
        {
            if (string.IsNullOrWhiteSpace(r.Id)) continue;
            float rad = r.Radius + 0.16f;
            for (int i = 0; i < r.Waypoints.Length - 1; i++)
            {
                if (RayIntersectsSegmentCapsule(ray, r.Waypoints[i], r.Waypoints[i + 1], rad, out float t) && t <= fineT + 0.05f)
                {
                    fineT = MathF.Min(fineT, t);
                    fineId = r.Id;
                }
            }
        }

        if (fineId is not null && (fineT < bestT + 1.5f || string.Equals(bestId, "WP-301", StringComparison.OrdinalIgnoreCase)))
            return fineId;

        return bestId;
    }

    public static bool RayIntersectsSegmentCapsule(Ray3D ray, Vector3 segA, Vector3 segB, float radius, out float tRay)
    {
        tRay = 0f;
        var u = ray.Direction;
        var v = segB - segA;
        var w = ray.Origin - segA;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);
        float denom = a * c - b * b;

        float sc, tc;
        if (denom < 1e-6f)
        {
            sc = 0f;
            tc = c > 1e-6f ? Math.Clamp(e / c, 0f, 1f) : 0f;
        }
        else
        {
            sc = (b * e - c * d) / denom;
            tc = (a * e - b * d) / denom;
            if (tc < 0f)
            {
                tc = 0f;
                sc = Math.Max(0f, -d / a);
            }
            else if (tc > 1f)
            {
                tc = 1f;
                sc = Math.Max(0f, (-d + b) / a);
            }
        }

        if (sc < 0f) return false;
        var closestRay = ray.Origin + u * sc;
        var closestSeg = segA + v * tc;
        if (Vector3.DistanceSquared(closestRay, closestSeg) <= radius * radius)
        {
            tRay = sc;
            return true;
        }
        return false;
    }

    public static bool RayIntersectsAabb(Ray3D ray, Vector3 min, Vector3 max, out float tNear)
    {
        tNear = 0f;
        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        if (!Slab(ray.Origin.X, ray.Direction.X, min.X, max.X, ref tMin, ref tMax)) return false;
        if (!Slab(ray.Origin.Y, ray.Direction.Y, min.Y, max.Y, ref tMin, ref tMax)) return false;
        if (!Slab(ray.Origin.Z, ray.Direction.Z, min.Z, max.Z, ref tMin, ref tMax)) return false;

        if (tMax < 0f) return false;
        tNear = tMin >= 0f ? tMin : tMax;
        return true;
    }

    private static bool Slab(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(dir) < 1e-6f)
            return origin >= min && origin <= max;

        float invD = 1f / dir;
        float t0 = (min - origin) * invD;
        float t1 = (max - origin) * invD;
        if (t0 > t1) (t0, t1) = (t1, t0);

        tMin = MathF.Max(tMin, t0);
        tMax = MathF.Min(tMax, t1);
        return tMax >= tMin;
    }
}
