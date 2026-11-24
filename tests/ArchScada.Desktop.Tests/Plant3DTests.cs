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
    public void GlbMeshLoader_LoadsAllSixteenIndustrialGlbModelsWithValidGeometry()
    {
        Plant3DSceneData.InvalidateMeshCache();
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();

        Assert.Equal(16, meshes.Count);

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
    public void ChppEngineeringConfig_LoadsConfirmedDefaultsAndNodeMetadata()
    {
        var cfg = ChppEngineeringConfig.Load();
        Assert.True(cfg.IsSiteConfirmed);
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
        Assert.Contains("Direct PLC: ROM101", Plant3DSceneData.FindNode("BN-101")!.PlcChannelMapping);
        Assert.Contains("Direct PLC: ROM101", Plant3DSceneData.FindNode("VF-101")!.PlcChannelMapping);
        Assert.Contains("Direct PLC: DRC101", Plant3DSceneData.FindNode("DRC-101")!.PlcChannelMapping);
        Assert.Contains("Direct PLC: CV_DRY", Plant3DSceneData.FindNode("CV-103")!.PlcChannelMapping);
        Assert.Contains("Direct PLC: CV_DRY", Plant3DSceneData.FindNode("BN-201")!.PlcChannelMapping);
        Assert.Contains("Direct PLC: DS_WASH", Plant3DSceneData.FindNode("DS-201")!.PlcChannelMapping);
        Assert.Contains("Direct PLC: DS_WASH", Plant3DSceneData.FindNode("CV-302")!.PlcChannelMapping);
        Assert.Contains("Derived Process Model (coupled to AF-001)", fel.PlcChannelMapping);
        Assert.StartsWith("[SITE CONFIRMED]", jc.ProvisionalConfigNote);

        var provisionalCfg = new ChppEngineeringConfig { IsSiteConfirmed = false };
        var provisionalNote = Plant3DSceneData.FormatProvisionalConfigNote(jc, provisionalCfg);
        Assert.StartsWith("[PROVISIONAL — Awaiting Site Confirmation]", provisionalNote);
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

        Assert.Equal(16, metrics.TotalGlbCount);
        Assert.Equal(16, metrics.Meshes.Count);
        Assert.True(metrics.TotalVertices > 0);
        Assert.True(metrics.TotalTriangles > 0);
        Assert.True(metrics.TotalGpuVboMiB > 0.0);
        Assert.True(metrics.ColdGlbLoadMs < 2000.0, $"Cold GLB load took {metrics.ColdGlbLoadMs} ms (expected < 2000 ms)");
        Assert.True(metrics.FlowAndPoseMicrosPerFrame < 500.0, $"Flow+Pose took {metrics.FlowAndPoseMicrosPerFrame} µs/frame (expected < 500 µs)");
    }

    [Fact]
    public void Plant3DSceneData_SampleConveyorCoalTrajectory_HasLinearBeltAndParabolicDischarge()
    {
        foreach (var conv in Plant3DSceneData.Conveyors)
        {
            var pStart = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, 0f);
            var pMid = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, 0.41f);
            var pHead = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, 0.82f);
            var pEnd = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, 1.0f);

            Assert.True(Vector3.Distance(pStart, conv.TailPos) < 1e-4f, $"{conv.Id} u=0 should be at TailPos");
            Assert.True(Vector3.Distance(pMid, Vector3.Lerp(conv.TailPos, conv.HeadPos, 0.5f)) < 1e-4f, $"{conv.Id} u=0.41 should be belt midpoint");
            Assert.True(Vector3.Distance(pHead, conv.HeadPos) < 1e-4f, $"{conv.Id} u=0.82 should be at HeadPos");
            Assert.True(Vector3.Distance(pEnd, conv.GetChuteBottomPos()) < 1e-3f, $"{conv.Id} u=1.0 should land at GetChuteBottomPos()");

            // Verify Newtonian downward acceleration d^2y/du^2 < 0 in the head-pulley discharge zone (0.82, 1.0]
            float u1 = 0.85f, u2 = 0.91f, u3 = 0.97f;
            float y1 = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, u1).Y;
            float y2 = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, u2).Y;
            float y3 = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, u3).Y;
            float secondDiff = y1 - 2f * y2 + y3;
            Assert.True(secondDiff < -1e-4f, $"{conv.Id} discharge arc must have downward acceleration d²y/du² < 0 (got {secondDiff})");
        }
    }

    [Fact]
    public void Plant3DSceneData_Tier1To3PhysicsAndPbrHelpers_DistinguishWetDryAndEmitPlumes()
    {
        // Wet vs dry stream classification and clearcoat PBR parameters
        Assert.False(Plant3DSceneData.IsWetCoalStream(CoalMaterialClass.RawRom));
        Assert.False(Plant3DSceneData.IsWetCoalStream(CoalMaterialClass.CrushedPrimary));
        Assert.False(Plant3DSceneData.IsWetCoalStream(CoalMaterialClass.PlantFeed));
        Assert.True(Plant3DSceneData.IsWetCoalStream(CoalMaterialClass.DeslimedCoal));
        Assert.True(Plant3DSceneData.IsWetCoalStream(CoalMaterialClass.CleanProduct));
        Assert.True(Plant3DSceneData.IsWetCoalStream(CoalMaterialClass.RefuseDiscard));

        var dryPbr = Plant3DSceneData.GetMaterialPbrParams(CoalMaterialClass.RawRom);
        var wetPbr = Plant3DSceneData.GetMaterialPbrParams(CoalMaterialClass.CleanProduct);
        Assert.Equal(0f, dryPbr.Clearcoat);
        Assert.True(dryPbr.Roughness > 0.7f);
        Assert.True(wetPbr.Clearcoat > 0.7f);
        Assert.True(wetPbr.Roughness < 0.4f);

        // Continuous belt burden thickness scales monotonically and is 0 when stopped
        Assert.Equal(0f, Plant3DSceneData.ComputeBeltBurdenThickness(CoalMaterialClass.RawRom, 0.0));
        float burdenLow = Plant3DSceneData.ComputeBeltBurdenThickness(CoalMaterialClass.PlantFeed, 0.35);
        float burdenHigh = Plant3DSceneData.ComputeBeltBurdenThickness(CoalMaterialClass.PlantFeed, 0.95);
        Assert.True(burdenLow > 0f && burdenHigh > burdenLow);

        // 3-axis elliptical screen vibration stays finite and handles NaN/Inf safely
        foreach (string screenId in new[] { "VF-101", "SS-101", "DS-201" })
        {
            var vib = Plant3DSceneData.ComputeScreenVibrationOffset(screenId, 2.34, 80.0);
            Assert.True(float.IsFinite(vib.X) && float.IsFinite(vib.Y) && float.IsFinite(vib.Z));
            Assert.True(vib.Length() > 0f && vib.Length() < 0.06f);
            Assert.Equal(Vector3.Zero, Plant3DSceneData.ComputeScreenVibrationOffset(screenId, double.NaN, 80.0));
        }

        // Fugitive dust + desliming water spray emission when plant is running vs stopped
        var runningFlows = new Dictionary<string, ProcessFlowState>(StringComparer.OrdinalIgnoreCase);
        var stoppedFlows = new Dictionary<string, ProcessFlowState>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Plant3DSceneData.AllNodes)
        {
            runningFlows[node.Id] = new ProcessFlowState(node.Id, 0.85, IsRunning: true, IsStarved: false, EstimatedTph: 500);
            stoppedFlows[node.Id] = new ProcessFlowState(node.Id, 0.0, IsRunning: false, IsStarved: true, EstimatedTph: 0);
        }

        var dumpPose = Plant3DSceneData.ComputeLoaderPose(4.8, active: true);
        var particles = Plant3DSceneData.ComputeDustAndSprayParticles(runningFlows, dumpPose, 4.8);
        Assert.Contains(particles, p => !p.IsWaterSpray);
        Assert.Contains(particles, p => p.IsWaterSpray);
        Assert.All(particles, p => Assert.True(float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && p.Alpha > 0f));

        var stoppedPose = Plant3DSceneData.ComputeLoaderPose(0.5, active: false);
        Assert.Empty(Plant3DSceneData.ComputeDustAndSprayParticles(stoppedFlows, stoppedPose, 4.8));

        // New effect toggles default to enabled
        var fx = new Unity3DEffectSettings();
        Assert.True(fx.ShowDustAndSprayPlumes);
        Assert.True(fx.EnableWetCoalClearcoat);
        Assert.True(fx.EnableContrastSharpening);
    }

    [Fact]
    public void Plant3DSceneData_Stage1And2_ElevatesBn101OnHillAndRunsCv101AndCv102Downwards()
    {
        var sp001 = Plant3DSceneData.FindNode("SP-001")!;
        var fel101 = Plant3DSceneData.FindNode("FEL-101")!;
        var bn101 = Plant3DSceneData.FindNode("BN-101")!;
        var vf101 = Plant3DSceneData.FindNode("VF-101")!;
        var jc101 = Plant3DSceneData.FindNode("JC-101")!;
        var ss101 = Plant3DSceneData.FindNode("SS-101")!;

        Assert.True(sp001.Position.Y >= 4.3f);
        Assert.True(fel101.Position.Y >= 4.3f);
        Assert.True(bn101.Position.Y >= 4.3f);
        Assert.True(vf101.Position.Y >= 4.3f);
        Assert.Equal(0f, jc101.Position.Y);
        Assert.Equal(0f, ss101.Position.Y);

        var cv101 = Plant3DSceneData.FindConveyor("CV-101")!;
        var cv102 = Plant3DSceneData.FindConveyor("CV-102")!;
        Assert.True(cv101.TailPos.Y > cv101.HeadPos.Y, "CV-101 Jaw feed conv must run downwards from elevated VF-101 to JC-101");
        Assert.True(cv102.TailPos.Y > cv102.HeadPos.Y, "CV-102 Under size conv must run downwards from elevated VF-101 to SS-101");

        for (double t = 0.0; t <= 12.0; t += 1.2)
        {
            var pose = Plant3DSceneData.ComputeLoaderPose(t, active: true);
            Assert.True(pose.ChassisPosition.Y >= 4.35f, $"Loader FEL-101 must remain on elevated ROM hill (t={t}, Y={pose.ChassisPosition.Y})");
        }
    }
}
