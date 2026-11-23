using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.Tests;

public class Plant3DTests
{
    [Fact]
    public void GlbMeshLoader_LoadsAllTwentyTwoIndustrialGlbModelsWithValidGeometry()
    {
        Plant3DSceneData.InvalidateMeshCache();
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();

        Assert.Equal(22, meshes.Count);

        string[] requiredModels =
        {
            "wheel_loader_chassis.glb",
            "wheel_loader_bucket.glb",
            "feeder_bin_bn101.glb",
            "vibro_feeder_vf101.glb",
            "jaw_crusher_jc101.glb",
            "double_roll_crusher_drc101.glb",
            "central_sizing_screen_ss101.glb",
            "desliming_screen_ds201.glb",
            "surge_bin_bn201.glb",
            "wash_plant_module_wp301.glb",
            "stockpile_cone.glb",
            "thickener_tk201.glb"
        };

        foreach (var model in requiredModels)
        {
            Assert.True(meshes.ContainsKey(model), $"Expected exported industrial GLB '{model}'");
        }

        foreach (var (name, mesh) in meshes)
        {
            Assert.True(mesh.VertexCount > 24, $"{name} should have multi-part vertex geometry");
            Assert.True(mesh.TriangleCount > 12, $"{name} should have valid triangles");
            Assert.True(mesh.BoundsMax.Y > mesh.BoundsMin.Y, $"{name} should have positive height");
        }
    }

    [Fact]
    public void Plant3DSceneData_CoversAllTwentyNineMachinesAndConveyorsWithValidScadaBindings()
    {
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();
        Assert.Equal(18, Plant3DSceneData.MachineNodes.Count);
        Assert.Equal(11, Plant3DSceneData.Conveyors.Count);
        Assert.Equal(29, Plant3DSceneData.AllNodes.Count);

        var tempDir = Path.Combine(Path.GetTempPath(), "archscada-3d-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var telemetry = new SimulatedTelemetryService(tempDir);
            var validScadaIds = telemetry.Equipment.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var node in Plant3DSceneData.AllNodes)
            {
                Assert.False(string.IsNullOrWhiteSpace(node.GaLabel), $"{node.Id} missing GaLabel");
                Assert.False(string.IsNullOrWhiteSpace(node.StageLabel), $"{node.Id} missing StageLabel");
                Assert.False(string.IsNullOrWhiteSpace(node.DimensionsSpec), $"{node.Id} missing DimensionsSpec");
                Assert.False(string.IsNullOrWhiteSpace(node.MechanicalPrinciple), $"{node.Id} missing MechanicalPrinciple");
                Assert.False(string.IsNullOrWhiteSpace(node.ComponentDescription), $"{node.Id} missing ComponentDescription");
                Assert.Contains(node.LinkedScadaId, validScadaIds);

                if (!node.IsConveyor)
                {
                    Assert.True(meshes.ContainsKey(node.GlbFileName), $"Machine {node.Id} missing GLB '{node.GlbFileName}'");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Plant3DSceneData_VerifiesStages1To6ProcessFlowAndCentralScreenConvergence()
    {
        var byId = Plant3DSceneData.AllNodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        // Stage 1: Wheel Loader -> Feeder Bin -> Vibro Feeder
        Assert.Contains("BN-101", byId["FEL-101"].DownstreamIds);
        Assert.Contains("VF-101", byId["BN-101"].DownstreamIds);

        // Stage 2: Vibro Feeder splits into Path A (CV-101 -> JC-101) and Path B (CV-102 -> SS-101 bypassing JC-101)
        Assert.Contains("CV-101", byId["VF-101"].DownstreamIds);
        Assert.Contains("CV-102", byId["VF-101"].DownstreamIds);
        Assert.Contains("JC-101", byId["CV-101"].DownstreamIds);
        Assert.DoesNotContain("JC-101", byId["CV-102"].DownstreamIds);
        Assert.Contains("SS-101", byId["CV-102"].DownstreamIds);

        // Stage 3-5: Jaw Crusher -> CV-103; Oversize CV-106 -> DRC-101 -> CV-104 -> SS-101
        Assert.Contains("CV-103", byId["JC-101"].DownstreamIds);
        Assert.Contains("DRC-101", byId["CV-106"].DownstreamIds);
        Assert.Contains("CV-104", byId["DRC-101"].DownstreamIds);
        Assert.Contains("SS-101", byId["CV-104"].DownstreamIds);

        // Stage 6: Central Sizing Screen (SS-101) receives BOTH CV-102 (Initial Undersize) and CV-104 (Final Sizing Feed)
        var ss101 = byId["SS-101"];
        Assert.Contains("CV-102", ss101.UpstreamIds);
        Assert.Contains("CV-104", ss101.UpstreamIds);
        Assert.Contains("CV-106", ss101.DownstreamIds); // Oversize return
        Assert.Contains("CV-201", ss101.DownstreamIds); // Plant feed conv
        Assert.Contains("CV-105", ss101.DownstreamIds); // Fines conv under screen
    }

    [Fact]
    public void Plant3DSceneData_ConveyorTransferPoints_HaveValidGeometryAndDropChutes()
    {
        var nodePos = Plant3DSceneData.MachineNodes.ToDictionary(n => n.Id, n => n.Position, StringComparer.OrdinalIgnoreCase);

        foreach (var conv in Plant3DSceneData.Conveyors)
        {
            float length = Vector3.Distance(conv.TailPos, conv.HeadPos);
            Assert.True(length > 2.0f, $"Conveyor {conv.Id} should have realistic industrial length (got {length:F2})");
            Assert.True(conv.HeadPos.Y >= conv.HeadDropTargetY, $"Conveyor {conv.Id} head pulley must be at or above transfer chute drop target");

            var fullPath = conv.GetFullFlowWaypoints();
            Assert.True(fullPath.Length >= 3, $"Conveyor {conv.Id} must include tail, head, and transfer chute discharge");

            // Check downstream machine proximity where downstream is a machine node
            foreach (var downId in conv.DownstreamIds)
            {
                if (nodePos.TryGetValue(downId, out var mPos))
                {
                    var chuteOut = conv.GetChuteBottomPos();
                    float horizDist = Vector2.Distance(new Vector2(chuteOut.X, chuteOut.Z), new Vector2(mPos.X, mPos.Z));
                    Assert.True(horizDist < 4.5f, $"Conveyor {conv.Id} discharge should align with downstream machine {downId} (dist={horizDist:F2}m)");
                }
            }
        }
    }

    [Fact]
    public void Plant3DSceneData_ComputeLoaderPose_ExecutesFullScoopClimbDumpReverseCycle()
    {
        var scoop = Plant3DSceneData.ComputeLoaderPose(0.5, active: true);    // t ~ 0.04
        var climb = Plant3DSceneData.ComputeLoaderPose(2.8, active: true);    // t ~ 0.23
        var dump = Plant3DSceneData.ComputeLoaderPose(4.8, active: true);     // t = 0.40
        var reverse = Plant3DSceneData.ComputeLoaderPose(7.4, active: true);  // t ~ 0.62

        Assert.False(scoop.IsDumpingCoal);
        Assert.True(climb.ChassisPosition.Z < scoop.ChassisPosition.Z, "Loader should drive North toward BN-101");
        Assert.True(dump.IsDumpingCoal, "Loader must actively dump coal at ramp crest");
        Assert.Equal(38f, dump.BoomAngleDegrees, 1);
        Assert.True(dump.BucketDumpAngleDegrees < -50f, "Bucket must tip forward during dump");
        Assert.True(dump.BinFillPulse > 0f, "Feeder bin level should surge on coal dump");
        Assert.True(reverse.ChassisPosition.Z > dump.ChassisPosition.Z, "Loader should reverse down ramp after dumping");
    }

    [Fact]
    public void Plant3DSceneData_EvaluateProcessFlows_PropagatesUpstreamStoppage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "archscada-flow-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var telemetry = new SimulatedTelemetryService(tempDir);
            var eqMap = telemetry.Equipment.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

            var runningFlows = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime: 4.8);
            Assert.True(runningFlows["SS-101"].NormalizedRate > 0.3, "Central sizing screen should receive active flow when plant is running");
            Assert.True(runningFlows["SS-101"].EstimatedTph > 200.0);

            // Stop ROM feeder & jaw crusher -> dry crushing/screening streams must drop
            eqMap["AF-001"].Status = EquipmentStatus.Stopped;
            eqMap["CR-101"].Status = EquipmentStatus.Stopped;

            var stoppedFlows = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime: 4.8);
            Assert.Equal(0.0, stoppedFlows["BN-101"].NormalizedRate);
            Assert.Equal(0.0, stoppedFlows["VF-101"].NormalizedRate);
            Assert.Equal(0.0, stoppedFlows["JC-101"].NormalizedRate);
            Assert.True(stoppedFlows["CV-101"].NormalizedRate < runningFlows["CV-101"].NormalizedRate);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, "SS-101")]
    [InlineData(true, "SS-101")]
    [InlineData(false, "JC-101")]
    [InlineData(false, "CV-102")]
    public void Plant3DSceneData_RaycastPicking_SelectsMachinesAndConveyors(bool isOrthographic, string targetId)
    {
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();
        var node = Plant3DSceneData.AllNodes.First(n => n.Id == targetId);

        var (view, proj, _) = Plant3DSceneData.BuildCameraMatrices(
            yaw: 0.0, pitch: 1.42, zoom: 0.72, targetX: node.Position.X, targetZ: node.Position.Z,
            width: 1000, height: 600, isOrthographic: isOrthographic);
        var vp = view * proj;

        var sampleWorld = node.IsConveyor ? node.Position : node.Position + new Vector3(0f, 1.2f, 0f);
        bool projected = Plant3DSceneData.TryProjectToScreen(sampleWorld, vp, 1000, 600, out var screenPt, out _);
        Assert.True(projected);

        var ray = Plant3DSceneData.UnprojectRay(screenPt, vp, 1000, 600);
        var picked = Plant3DSceneData.PickEquipment(ray, meshes);

        Assert.Equal(targetId, picked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plant3DSceneData_BuildCameraMatrices_MapsNearAndFarToOpenGlClipSpace(bool isOrthographic)
    {
        var (view, proj, camPos) = Plant3DSceneData.BuildCameraMatrices(
            yaw: -0.62, pitch: 0.52, zoom: 1.05, targetX: 0.0, targetZ: 0.5,
            width: 800, height: 420, isOrthographic: isOrthographic);
        var vp = view * proj;

        var target = new Vector3(0.0f, 0.9f, 0.5f);
        var forward = Vector3.Normalize(target - camPos);

        float near = isOrthographic ? 0.1f : 0.2f;
        float far = 140f;

        var nearWorld = camPos + forward * near;
        var farWorld = camPos + forward * far;

        var nearClip = Vector4.Transform(new Vector4(nearWorld, 1f), vp);
        var farClip = Vector4.Transform(new Vector4(farWorld, 1f), vp);

        float nearNdcZ = nearClip.Z / nearClip.W;
        float farNdcZ = farClip.Z / farClip.W;

        Assert.InRange(nearNdcZ, -1.0001f, -0.9999f);
        Assert.InRange(farNdcZ, 0.9999f, 1.0001f);
    }

    [Fact]
    public void ChppEngineeringConfig_LoadsProvisionalDefaultsAndNodeMetadata()
    {
        var cfg = ChppEngineeringConfig.Load();
        Assert.False(cfg.IsSiteConfirmed);
        Assert.Equal(ChppEngineeringConfig.RoutingBypassToCv104, cfg.JawDischargeRoutingMode);
        Assert.Equal(ChppEngineeringConfig.FinesDryThermalBlend, cfg.DryFinesDestinationMode);
        Assert.Equal(75.0, cfg.GrizzlyApertureMm);
        Assert.Equal(50.0, cfg.SizingTopDeckApertureMm);
        Assert.Equal(6.0, cfg.SizingBottomDeckApertureMm);
        Assert.Equal(0.5, cfg.DeslimingApertureMm);
        Assert.Equal(1200, cfg.PrimaryBeltWidthMm);
        Assert.Equal(900, cfg.SecondaryBeltWidthMm);
        Assert.True(cfg.RatedMotorKw.ContainsKey("JC-101"));
        Assert.True(cfg.RatedMotorKw.ContainsKey("CV-101"));
        Assert.True(cfg.RatedMotorKw.ContainsKey("CV-302"));

        var jc = Plant3DSceneData.FindNode("JC-101")!;
        var fel = Plant3DSceneData.FindNode("FEL-101")!;
        Assert.Contains("Direct PLC: CR101", jc.PlcChannelMapping);
        Assert.Contains("10.10.1.11:502", jc.PlcChannelMapping);
        Assert.Contains("Derived Process Model (coupled to AF-001)", fel.PlcChannelMapping);
        Assert.StartsWith("[PROVISIONAL — Awaiting Site Confirmation]", jc.ProvisionalConfigNote);

        var confirmedCfg = new ChppEngineeringConfig { IsSiteConfirmed = true };
        var confirmedNote = Plant3DSceneData.FormatProvisionalConfigNote(jc, confirmedCfg);
        Assert.StartsWith("[SITE CONFIRMED]", confirmedNote);
    }

    [Fact]
    public void Plant3DSceneData_EvaluateProcessFlows_IsolatesIndividualNodeFaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "archscada-fault-iso-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var telemetry = new SimulatedTelemetryService(tempDir);
            var eqMap = telemetry.Equipment.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
            var cfg = new ChppEngineeringConfig
            {
                JawDischargeRoutingMode = ChppEngineeringConfig.RoutingDivertThroughDoubleRollCrusher
            };

            var baseline = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime: 4.8, cfg);

            // Trip CV-102 locally -> Stream #1 starves, Path A still feeds SS-101 (~55% rate)
            var tripCv102 = Plant3DSceneData.EvaluateProcessFlows(
                eqMap, simTime: 4.8, cfg,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CV-102" });
            Assert.Equal(0.0, tripCv102["CV-102"].NormalizedRate);
            Assert.True(tripCv102["CV-104"].NormalizedRate > 0.5);
            Assert.InRange(tripCv102["SS-101"].NormalizedRate / baseline["SS-101"].NormalizedRate, 0.35, 0.70);

            // Trip JC-101 locally -> Path A starves, Path B (CV-102) still feeds SS-101 (~45% rate)
            var tripJc101 = Plant3DSceneData.EvaluateProcessFlows(
                eqMap, simTime: 4.8, cfg,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "JC-101" });
            Assert.Equal(0.0, tripJc101["JC-101"].NormalizedRate);
            Assert.Equal(0.0, tripJc101["CV-103"].NormalizedRate);
            Assert.True(tripJc101["CV-103"].IsStarved);
            Assert.True(tripJc101["CV-102"].NormalizedRate > 0.5);
            Assert.InRange(tripJc101["SS-101"].NormalizedRate / baseline["SS-101"].NormalizedRate, 0.35, 0.65);

            // Trip BOTH CV-102 and JC-101 -> SS-101, CV-201, DS-201, and WP-301 are all starved
            var tripBoth = Plant3DSceneData.EvaluateProcessFlows(
                eqMap, simTime: 4.8, cfg,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CV-102", "JC-101" });
            Assert.True(tripBoth["SS-101"].IsStarved);
            Assert.True(tripBoth["CV-201"].IsStarved);
            Assert.True(tripBoth["DS-201"].IsStarved);
            Assert.True(tripBoth["WP-301"].IsStarved);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Plant3DSceneData_EvaluateProcessFlows_RespectsJawDischargeRoutingModeOnDrcFault()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "archscada-routing-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var telemetry = new SimulatedTelemetryService(tempDir);
            var eqMap = telemetry.Equipment.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
            var drcFault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DRC-101" };

            var bypassCfg = new ChppEngineeringConfig
            {
                JawDischargeRoutingMode = ChppEngineeringConfig.RoutingBypassToCv104
            };
            var bypassFlow = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime: 4.8, bypassCfg, drcFault);
            Assert.Equal(0.0, bypassFlow["DRC-101"].NormalizedRate);
            Assert.True(bypassFlow["CV-103"].NormalizedRate > 0.5);
            Assert.True(bypassFlow["CV-104"].NormalizedRate > 0.5, "BypassToCv104 must preserve CV-103 -> CV-104 flow when DRC-101 faults");

            var divertCfg = new ChppEngineeringConfig
            {
                JawDischargeRoutingMode = ChppEngineeringConfig.RoutingDivertThroughDoubleRollCrusher
            };
            var divertFlow = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime: 4.8, divertCfg, drcFault);
            Assert.Equal(0.0, divertFlow["DRC-101"].NormalizedRate);
            Assert.Equal(0.0, divertFlow["CV-104"].NormalizedRate);
            Assert.True(divertFlow["CV-104"].IsStarved, "DivertThroughDoubleRollCrusher must halt CV-104 when DRC-101 faults");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Plant3DSceneData_BenchmarkScenePerformance_MeetsLatencyAndBudgetTargets()
    {
        var metrics = Plant3DSceneData.BenchmarkScenePerformance();

        Assert.Equal(22, metrics.TotalGlbCount);
        Assert.Equal(22, metrics.Meshes.Count);
        Assert.True(metrics.TotalVertices > 0);
        Assert.True(metrics.TotalTriangles > 0);
        Assert.True(metrics.TotalGpuVboMiB > 0.0);
        Assert.True(metrics.ColdGlbLoadMs < 2000.0, $"Cold GLB load took {metrics.ColdGlbLoadMs} ms (expected < 2000 ms)");
        Assert.True(metrics.FlowAndPoseMicrosPerFrame < 500.0, $"Flow+Pose took {metrics.FlowAndPoseMicrosPerFrame} µs/frame (expected < 500 µs)");
    }
}
