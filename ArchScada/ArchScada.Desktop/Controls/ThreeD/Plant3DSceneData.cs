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
    bool IsConveyor = false)
{
    public string UpstreamDisplay => UpstreamIds.Length == 0 ? "ROM Source" : string.Join(", ", UpstreamIds);
    public string DownstreamDisplay => DownstreamIds.Length == 0 ? "Final Stockpile / Product" : string.Join(", ", DownstreamIds);
    public string FlowSummary => $"{UpstreamDisplay}  →  {Id}  →  {DownstreamDisplay}";
    public string PlcChannelMapping => Plant3DSceneData.GetPlcChannelMapping(Id, LinkedScadaId);
    public string ProvisionalConfigNote => Plant3DSceneData.FormatProvisionalConfigNote(this, ChppEngineeringConfig.Current);
}

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
    Vector3? HeadDischargeOffset = null)
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
    Vector3[] Waypoints);

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
public static class Plant3DSceneData
{
    public static readonly IReadOnlyList<Plant3DNode> MachineNodes = new Plant3DNode[]
    {
        // Stage 1 — ROM Reception
        new(
            Id: "SP-001",
            GaLabel: "ROM Raw Coal Stockpile",
            StageLabel: "Stage 1 — ROM Reception",
            EquipmentType: "Stockpile",
            GlbFileName: "stockpile_cone.glb",
            Position: new(-13.8f, 0f, 16.4f),
            YawDegrees: 0f,
            Scale: new(1.1f, 0.9f, 1.1f),
            HalfExtents: new(2.1f, 1.35f, 2.1f),
            LabelHeight: 2.9f,
            DefaultColor: Color.Parse("#475569"),
            DimensionsSpec: "Ø 16 m × 5.2 m cone (4.2 m × 2.7 m model)",
            MechanicalPrinciple: "Bulk run-of-mine coal stockpile reclaimed via front-end wheel loader bucket cycles.",
            ComponentDescription: "Unscreened ROM raw coal reserve (0–300 mm) feeding Stage 1 primary hopper loading.",
            UpstreamIds: Array.Empty<string>(),
            DownstreamIds: new[] { "FEL-101" },
            LinkedScadaId: "AF-001"),

        new(
            Id: "FEL-101",
            GaLabel: "Front-End Wheel Loader",
            StageLabel: "Stage 1 — ROM Reception",
            EquipmentType: "Mobile Wheel Loader",
            GlbFileName: "wheel_loader_chassis.glb",
            Position: new(-13.8f, 0f, 13.4f),
            YawDegrees: 90f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.1f, 1.1f, 1.4f),
            LabelHeight: 2.6f,
            DefaultColor: Color.Parse("#EAB308"),
            DimensionsSpec: "3.4 m × 1.9 m × 2.4 m · 4.5 m³ rock bucket",
            MechanicalPrinciple: "4-stage shuttle cycle: scoop ROM at SP-001, climb BN-101 earthen ramp, raise boom (38°), tilt bucket (-58°) to dump, reverse.",
            ComponentDescription: "Articulated diesel wheel loader with hydraulic lift arms and high-dump coal bucket.",
            UpstreamIds: new[] { "SP-001" },
            DownstreamIds: new[] { "BN-101" },
            LinkedScadaId: "AF-001"),

        new(
            Id: "BN-101",
            GaLabel: "Feeder Bin / Receiving Hopper",
            StageLabel: "Stage 1 — ROM Reception",
            EquipmentType: "Receiving Hopper",
            GlbFileName: "feeder_bin_bn101.glb",
            Position: new(-13.8f, 0f, 8.8f),
            YawDegrees: 90f, // Right-handed CreateRotationY(+90°) maps -X ramp South (+Z) toward FEL-101
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.7f, 1.55f, 1.9f),
            LabelHeight: 3.35f,
            DefaultColor: Color.Parse("#B45309"),
            DimensionsSpec: "4.0 m × 3.6 m × 3.2 m · 45 t surge capacity",
            MechanicalPrinciple: "Gravity surge hopper with inclined steel dump ramp and wear-lined discharge throat onto grizzly feeder.",
            ComponentDescription: "Heavy-plate ROM receiving bin with loader ramp (-X rotated South toward FEL-101) and surge level monitoring.",
            UpstreamIds: new[] { "FEL-101" },
            DownstreamIds: new[] { "VF-101" },
            LinkedScadaId: "AF-001",
            HasFillLevel: true,
            FillRadius: 0.95f,
            FillBaseY: 1.45f,
            FillMaxHeight: 1.35f),

        // Stage 2 — Initial Separation
        new(
            Id: "VF-101",
            GaLabel: "Primary Vibro Grizzly Feeder",
            StageLabel: "Stage 2 — Initial Separation",
            EquipmentType: "Vibrating Grizzly Feeder",
            GlbFileName: "vibro_feeder_vf101.glb",
            Position: new(-13.8f, 0.2f, 6.2f),
            YawDegrees: 90f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(1.1f, 0.95f, 1.4f),
            LabelHeight: 2.35f,
            DefaultColor: Color.Parse("#0284C7"),
            DimensionsSpec: "3.2 m × 1.8 m × 1.7 m · 75 mm grizzly bar aperture",
            MechanicalPrinciple: "Dual eccentric unbalanced motors impart linear throw, scalping <75 mm fines onto CV-102 while advancing +75 mm oversize onto CV-101.",
            ComponentDescription: "Heavy-duty tapered-bar grizzly feeder splitting ROM coal into Path A (Jaw Feed) and Path B (Undersize Bypass).",
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
            GaLabel: "Dense-Medium Wash Plant, Cyclone, Dewatering Screen, Control Room & MCC",
            StageLabel: "Wash Plant & Medium Recovery",
            EquipmentType: "Dense-Medium Cyclone Module",
            GlbFileName: "wash_plant_module_wp301.glb",
            Position: new(-0.8f, 0f, -7.8f),
            YawDegrees: 0f,
            Scale: new(1.0f, 1.0f, 1.0f),
            HalfExtents: new(3.3f, 2.35f, 2.2f),
            LabelHeight: 4.95f,
            DefaultColor: Color.Parse("#1D4ED8"),
            DimensionsSpec: "6.8 m × 4.4 m × 4.7 m multi-level module",
            MechanicalPrinciple: "Centrifugal dense-medium cyclone separates low-density clean coal (overflow) from high-density shale discard (underflow) at SG 1.52, followed by drain-and-rinse dewatering.",
            ComponentDescription: "Integrated wash plant structure housing mixing box, DMC cyclone, clean coal & discard dewatering screens, and elevated MCC / Control Room.",
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
            DefaultColor: Color.Parse("#22C55E"),
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
            DefaultColor: Color.Parse("#EF4444"),
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
            TailPos: new(-13.8f, 0.85f, 5.4f),
            HeadPos: new(-13.8f, 3.55f, 0.8f),
            Width: 0.95f,
            HeadDropTargetY: 2.15f,
            MaterialClass: CoalMaterialClass.RawRom,
            LinkedScadaId: "CV-101",
            UpstreamIds: new[] { "VF-101" },
            DownstreamIds: new[] { "JC-101" },
            DimensionsSpec: "5.3 m inclined · 1200 mm belt · 35° trough",
            MechanicalPrinciple: "Troughed belt elevating +75 mm grizzly oversize from VF-101 into Primary Jaw Crusher JC-101 feed throat.",
            ComponentDescription: "Stage 2 Path A conveyor with tail skirtboard, safety walkway, and enclosed head chute into JC-101.",
            HeadDischargeOffset: new(0f, 0f, -0.95f)),

        new(
            Id: "CV-102",
            GaLabel: "Under size conv",
            StageLabel: "Stage 2 — Initial Separation",
            TailPos: new(-12.8f, 0.65f, 6.1f),
            HeadPos: new(-1.9f, 4.15f, 1.15f),
            Width: 0.85f,
            HeadDropTargetY: 2.95f,
            MaterialClass: CoalMaterialClass.ScalpedUndersize,
            LinkedScadaId: "CV-102",
            UpstreamIds: new[] { "VF-101" },
            DownstreamIds: new[] { "SS-101" },
            DimensionsSpec: "12.5 m diagonal NE · 900 mm belt · 16° incline",
            MechanicalPrinciple: "Scalped <75 mm grizzly undersize bypasses crushing and conveys directly NE into Central Sizing Screen SS-101 dual feed box (Stream #1).",
            ComponentDescription: "Stage 2 Path B bypass conveyor with structural support bents and maintenance walkway.",
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
            GaLabel: "Cyclone feed conv",
            StageLabel: "Wash Plant & Medium Recovery",
            TailPos: new(7.2f, 1.85f, -9.6f),
            HeadPos: new(2.6f, 3.65f, -8.1f),
            Width: 0.90f,
            HeadDropTargetY: 2.35f,
            MaterialClass: CoalMaterialClass.DeslimedCoal,
            LinkedScadaId: "DM-301",
            UpstreamIds: new[] { "DS-201" },
            DownstreamIds: new[] { "WP-301" },
            DimensionsSpec: "5.2 m WSW · 1050 mm belt · 20° incline",
            MechanicalPrinciple: "Delivers deslimed +0.5 mm coal from DS-201 into WP-301 Dense-Medium Mixing Box.",
            ComponentDescription: "Wash plant cyclone feed conveyor linking desliming screen and dense-medium module.",
            HeadDischargeOffset: new(-0.85f, 0f, 0.25f)),

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
        foreach (var n in AllNodes)
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
        // WP-301 -> MS-301 (Dilute medium to Magnetic Separator)
        new("MS-301", FlowMedium.Magnetite, 0.07f, new Vector3[]
        {
            new(-1.0f, 1.35f, -9.2f),
            new(-1.0f, 1.35f, -11.4f),
            new(-1.6f, 1.35f, -11.4f)
        }),
        // MS-301 -> TK-301 (Recovered Magnetite Concentrate)
        new("MS-301", FlowMedium.Magnetite, 0.07f, new Vector3[]
        {
            new(-2.3f, 1.10f, -11.4f),
            new(-5.4f, 1.10f, -11.4f),
            new(-5.4f, 1.10f, -12.0f)
        }),
        // TK-301 -> WP-301 (Corrected Dense Medium to Mixing Box)
        new("DM-301", FlowMedium.Magnetite, 0.08f, new Vector3[]
        {
            new(-5.4f, 1.45f, -12.0f),
            new(-5.4f, 1.45f, -7.8f),
            new(-2.4f, 1.45f, -7.8f)
        }),
        // WP-301 -> TK-201 (Slimes Effluent to Thickener Feedwell)
        new("WP-301", FlowMedium.Slurry, 0.08f, new Vector3[]
        {
            new(-0.8f, 0.90f, -9.4f),
            new(-0.8f, 0.90f, -13.5f),
            new(-1.2f, 1.85f, -15.8f)
        }),
        // TK-201 -> PP-201 -> TK-301 (Thickener Underflow)
        new("PP-201", FlowMedium.Slurry, 0.075f, new Vector3[]
        {
            new(-1.2f, 0.45f, -15.8f),
            new(-4.6f, 0.45f, -15.8f),
            new(-4.6f, 0.45f, -15.0f),
            new(-5.4f, 0.95f, -15.0f),
            new(-5.4f, 0.95f, -12.0f)
        }),
        // TK-201 -> PP-203 -> WP-301 (Clarified Process Water & Flocculant Return)
        new("PP-203", FlowMedium.Froth, 0.07f, new Vector3[]
        {
            new(1.4f, 1.45f, -15.8f),
            new(2.2f, 1.45f, -15.8f),
            new(2.2f, 0.65f, -15.0f),
            new(2.2f, 1.10f, -8.8f),
            new(0.8f, 1.10f, -8.8f)
        })
    };

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
    /// Computes the 4-stage articulated wheel loader pose for FEL-101:
    ///   [0.00..0.14): Scoop ROM at SP-001 (Z = 12.6)
    ///   [0.14..0.32): Drive North and climb BN-101 ramp (Z = 12.6 -> 10.1, Y = 0 -> 0.85) while lifting boom (0 -> 38 deg)
    ///   [0.32..0.48]: Dump raw coal boulders into BN-101 (boom 38 deg, bucket dump 0 -> -58 deg)
    ///   [0.48..0.74): Reverse down ramp (Z = 10.1 -> 12.6, Y = 0.85 -> 0) and lower boom
    ///   [0.74..1.00): Turn & reposition at SP-001 for next scoop
    /// </summary>
    public static LoaderPose ComputeLoaderPose(double simTime, bool active)
    {
        if (!active)
        {
            return new LoaderPose(
                ChassisPosition: new(-13.8f, 0f, 11.8f),
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
        const float yRampTop = 0.88f;

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
            y = 0f;
            yaw = -90f;
            pitch = 0f;
            boom = 4f + u * 6f;
            dump = 12f * (1f - u);
        }
        else if (t < 0.32f)
        {
            // Stage 2: Turn toward BN-101 (North -Z, yaw +90) and climb ramp to Z=11.75, Y=0.88
            float u = Smooth01((t - 0.14f) / 0.18f);
            float turnU = Smooth01(Math.Clamp((t - 0.14f) / 0.08f, 0f, 1f));
            z = Lerp(zStockpile, zRampTop, u);
            float rampFrac = Math.Clamp((zRampToe - z) / (zRampToe - zRampTop), 0f, 1f);
            y = rampFrac * yRampTop;
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
            y = yRampTop;
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
            // Stage 4: Reverse down ramp to stockpile staging Z=14.3
            float u = Smooth01((t - 0.48f) / 0.26f);
            z = Lerp(zRampTop, zStockpile, u);
            float rampFrac = Math.Clamp((zRampToe - z) / (zRampToe - zRampTop), 0f, 1f);
            y = rampFrac * yRampTop;
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
            y = 0f;
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
        "BN-101" => "Direct PLC: ROM101 (Modbus 10.10.1.15:502 · Cnl 141–142)",
        "VF-101" => "Direct PLC: ROM101 (Modbus 10.10.1.15:502 · Cnl 143)",
        "JC-101" => "Direct PLC: CR101 (Modbus 10.10.1.11:502 · Cnl 101–103)",
        "DRC-101" => "Direct PLC: DRC101 (Modbus 10.10.1.16:502 · Cnl 151–153)",
        "SS-101" => "Direct PLC: SC102 (Modbus 10.10.1.12:502 · Cnl 111–112)",
        "CV-101" => "Direct PLC: CV101 (Modbus 10.10.1.13:502 · Cnl 121–122, 711)",
        "CV-102" => "Direct PLC: CV102 (Modbus 10.10.1.14:502 · Cnl 131–132, 712)",
        "CV-103" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 161)",
        "CV-104" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 162)",
        "CV-105" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 163)",
        "CV-106" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 164)",
        "CV-201" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 165)",
        "BN-201" => "Direct PLC: CV_DRY (Modbus 10.10.1.17:502 · Cnl 166)",
        "CV-202" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 311)",
        "DS-201" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 312–313)",
        "CV-203" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 314)",
        "CV-301" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 315)",
        "CV-302" => "Direct PLC: DS_WASH (Modbus 10.10.3.32:502 · Cnl 316)",
        "TK-201" => "Direct PLC: TK201 (Modbus 10.10.2.21:502 · Cnl 201–203)",
        "PP-201" => "Direct PLC: PP201 (Modbus 10.10.2.22:502 · Cnl 211–212)",
        "PP-202" => "Direct PLC: PP202 (Modbus 10.10.2.23:502 · Cnl 221)",
        "PP-203" => "Direct PLC: PP203 (Modbus 10.10.2.24:502 · Cnl 231)",
        "TK-301" or "MS-301" or "WP-301" => "Direct PLC: TK301/DMC (Modbus 10.10.3.31:502 · Cnl 301–305)",
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
        FlowMedium.Coal => Color.Parse("#F59E0B"),
        FlowMedium.Froth => Color.Parse("#22C55E"),
        FlowMedium.Magnetite => Color.Parse("#14B8A6"),
        _ => Color.Parse("#38BDF8")
    };

    public static Color GetMaterialColor(CoalMaterialClass mat) => mat switch
    {
        CoalMaterialClass.RawRom => Color.Parse("#A16207"),
        CoalMaterialClass.ScalpedUndersize => Color.Parse("#64748B"),
        CoalMaterialClass.CrushedPrimary => Color.Parse("#94A3B8"),
        CoalMaterialClass.ScreenOversize => Color.Parse("#F59E0B"),
        CoalMaterialClass.CrushedRoll => Color.Parse("#CBD5E1"),
        CoalMaterialClass.SizedFines => Color.Parse("#38BDF8"),
        CoalMaterialClass.PlantFeed => Color.Parse("#60A5FA"),
        CoalMaterialClass.DeslimedCoal => Color.Parse("#2DD4BF"),
        CoalMaterialClass.CleanProduct => Color.Parse("#22C55E"),
        CoalMaterialClass.RefuseDiscard => Color.Parse("#EF4444"),
        _ => Color.Parse("#F59E0B")
    };

    public static float GetMaterialLumpSize(CoalMaterialClass mat) => mat switch
    {
        CoalMaterialClass.RawRom => 0.23f,
        CoalMaterialClass.ScreenOversize => 0.21f,
        CoalMaterialClass.CrushedPrimary => 0.17f,
        CoalMaterialClass.RefuseDiscard => 0.16f,
        CoalMaterialClass.ScalpedUndersize => 0.15f,
        CoalMaterialClass.CrushedRoll => 0.13f,
        CoalMaterialClass.PlantFeed => 0.13f,
        CoalMaterialClass.DeslimedCoal => 0.12f,
        CoalMaterialClass.CleanProduct => 0.12f,
        CoalMaterialClass.SizedFines => 0.09f,
        _ => 0.14f
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
    /// Samples a coal parcel along a conveyor: linear belt travel from TailPos to HeadPos for u in [0, 0.82],
    /// followed by a Newtonian parabolic projectile discharge arc into the enclosed transfer chute for u in (0.82, 1.0].
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

        float dx = freeX - cx;
        float dz = freeZ - cz;
        float dev = MathF.Sqrt(dx * dx + dz * dz);
        float maxDev = MathF.Min(0.24f, conv.Width * 0.30f) * (1f - tauNorm);
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
        double width, double height, bool isOrthographic)
    {
        float dist = (float)(19.0 / Math.Clamp(zoom, 0.35, 3.2));
        var target = new Vector3((float)targetX, 0.9f, (float)targetZ);

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
