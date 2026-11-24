using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.Tests;

public class EngineStabilityTests
{
    [Fact]
    public void CoupledProcessAndAlarmSoak_500Ticks_RemainsFiniteBoundedAndRecoversFromFault()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "archscada-soak-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var telemetry = new SimulatedTelemetryService(tempDir);
            var coupled = new CoupledPlantProcessModel();
            var alarmEngine = new Isa182AlarmEngine();
            var activeAlarms = new List<AlarmItem>();

            var eqMap = telemetry.Equipment.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
            var tagMap = telemetry.AllTags.ToDictionary(t => t.TagId, StringComparer.OrdinalIgnoreCase);

            // Establish healthy baseline before fault injection
            tagMap["CV102_TMP"].Value = 58.0;
            eqMap["CV-102"].Status = EquipmentStatus.Running;

            double baselineScreenRate = 0.0;

            for (int tick = 0; tick < 500; tick++)
            {
                double simTime = tick * 0.25;

                // Inject a temporary fault window between ticks 100 and 149
                if (tick == 100)
                {
                    eqMap["CR-101"].Status = EquipmentStatus.Fault;
                    tagMap["CR101_BRG"].Value = 90.0; // > HH 86.0 °C
                    tagMap["CR101_TPH"].Value = 0.0;
                }
                else if (tick == 150)
                {
                    // Clear fault, restore nominal bearing temp and throughput, and acknowledge alarms
                    eqMap["CR-101"].Status = EquipmentStatus.Running;
                    tagMap["CR101_BRG"].Value = 64.0;
                    tagMap["CR101_TPH"].Value = 642.5;
                    alarmEngine.AcknowledgeAll(activeAlarms);
                }

                // Gentle bounded process variation during normal ticks
                if (tick < 100 || tick >= 150)
                {
                    tagMap["CR101_TPH"].Value = 642.5 + Math.Sin(tick * 0.1) * 18.0;
                    tagMap["TK301_SG"].Value = 1.52 + Math.Cos(tick * 0.08) * 0.02;
                    tagMap["PP401_DOS"].Value = 14.8 + Math.Sin(tick * 0.12) * 0.6;
                    tagMap["PP203_DOS"].Value = 24.5 + Math.Cos(tick * 0.09) * 1.2;
                }

                coupled.StepCoupledPhysics(
                    tagMap,
                    out double yieldPct,
                    out double ashPct,
                    out double waterRecPct,
                    out double specEnergy);

                alarmEngine.Evaluate(telemetry.AllTags, activeAlarms);
                var flows = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime);
                var pose = Plant3DSceneData.ComputeLoaderPose(simTime, active: true);

                // Assert zero NaN/Infinity across coupled outputs and all tags
                Assert.True(double.IsFinite(yieldPct) && yieldPct is >= 70.0 and <= 92.0);
                Assert.True(double.IsFinite(ashPct) && ashPct is >= 6.0 and <= 16.0);
                Assert.True(double.IsFinite(waterRecPct) && waterRecPct is >= 75.0 and <= 96.0);
                Assert.True(double.IsFinite(specEnergy) && specEnergy >= 0.0);

                foreach (var tag in telemetry.AllTags)
                {
                    Assert.True(double.IsFinite(tag.Value), $"Tick {tick}: tag {tag.TagId} became non-finite ({tag.Value})");
                    Assert.InRange(tag.Value, tag.MinLimit, tag.MaxLimit);
                }

                // Bounded temperatures, levels, pressures
                Assert.InRange(tagMap["CR101_BRG"].Value, 20.0, 95.0);
                Assert.InRange(tagMap["CV102_TMP"].Value, 20.0, 90.0);
                Assert.InRange(tagMap["TK201_LVL"].Value, 0.0, 100.0);
                Assert.InRange(tagMap["SL601_LVL"].Value, 0.0, 100.0);
                Assert.InRange(tagMap["DM301_PRS"].Value, 0.0, 260.0);
                Assert.InRange(tagMap["PP201_PRS"].Value, 0.0, 8.0);

                // Verify loader pose remains finite
                Assert.True(float.IsFinite(pose.ChassisPosition.X) && float.IsFinite(pose.ChassisPosition.Y) && float.IsFinite(pose.ChassisPosition.Z));
                Assert.True(float.IsFinite(pose.YawDegrees) && float.IsFinite(pose.BoomAngleDegrees) && float.IsFinite(pose.BucketDumpAngleDegrees));

                if (tick == 99)
                {
                    baselineScreenRate = flows["SS-101"].NormalizedRate;
                }

                if (tick == 110)
                {
                    // Faulted CR-101 must trigger ALM-CR101_BRG and stop JC-101
                    Assert.Equal(IsaAlarmState.UnacknowledgedActive, alarmEngine.GetState("ALM-CR101_BRG"));
                    Assert.Equal(0.0, flows["JC-101"].NormalizedRate);
                }

                if (tick >= 200)
                {
                    // Deterministic recovery after fault clear
                    Assert.Equal(IsaAlarmState.Normal, alarmEngine.GetState("ALM-CR101_BRG"));
                    Assert.True(flows["JC-101"].NormalizedRate > 0.3);
                    Assert.True(flows["SS-101"].NormalizedRate > 0.3);
                }
            }

            // Re-run an identical nominal state post-recovery and confirm deterministic flow convergence
            var recoveredFlows = Plant3DSceneData.EvaluateProcessFlows(eqMap, 99 * 0.25);
            Assert.Equal(baselineScreenRate, recoveredFlows["SS-101"].NormalizedRate, precision: 6);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtremeAndMalformedTelemetryInputs_ProduceFiniteClampedOutputsWithoutThrowing()
    {
        double[] malformedLoads =
        [
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            -50.0,
            250.0
        ];

        foreach (double badLoad in malformedLoads)
        {
            var eqMap = new Dictionary<string, EquipmentItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var scadaId in Plant3DSceneData.AllNodes.Select(n => n.LinkedScadaId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                eqMap[scadaId] = new EquipmentItem
                {
                    Id = scadaId,
                    Status = EquipmentStatus.Running,
                    LoadPercent = badLoad
                };
            }

            var flows = Plant3DSceneData.EvaluateProcessFlows(eqMap, simTime: badLoad);
            Assert.Equal(Plant3DSceneData.AllNodes.Count, flows.Count);

            foreach (var flow in flows.Values)
            {
                Assert.True(double.IsFinite(flow.NormalizedRate), $"NormalizedRate not finite for load={badLoad}");
                Assert.InRange(flow.NormalizedRate, 0.0, 1.35);
                Assert.True(double.IsFinite(flow.EstimatedTph) && flow.EstimatedTph >= 0.0, $"EstimatedTph not finite for load={badLoad}");
            }
        }

        float[] extremeU = [float.NaN, float.NegativeInfinity, float.PositiveInfinity, -5.0f, 0.0f, 0.82f, 1.0f, 5.0f];
        foreach (var conv in Plant3DSceneData.Conveyors)
        {
            foreach (float u in extremeU)
            {
                Vector3 pt = Plant3DSceneData.SampleConveyorCoalTrajectory(conv, u);
                Assert.True(float.IsFinite(pt.X), $"X not finite for {conv.Id} u={u}");
                Assert.True(float.IsFinite(pt.Y), $"Y not finite for {conv.Id} u={u}");
                Assert.True(float.IsFinite(pt.Z), $"Z not finite for {conv.Id} u={u}");
            }
        }
    }

    [Fact]
    public async Task ConcurrentMultiThread3DEngine_16Tasks_IsThreadSafeAndDeterministic()
    {
        Plant3DSceneData.InvalidateMeshCache();

        var eqMap = new Dictionary<string, EquipmentItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var scadaId in Plant3DSceneData.AllNodes.Select(n => n.LinkedScadaId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            eqMap[scadaId] = new EquipmentItem
            {
                Id = scadaId,
                Status = EquipmentStatus.Running,
                LoadPercent = 75.0
            };
        }

        var targetNode = Plant3DSceneData.AllNodes.First(n => n.Id == "SS-101");
        var (view, proj, _) = Plant3DSceneData.BuildCameraMatrices(
            yaw: 0.0, pitch: 1.42, zoom: 0.72, targetX: targetNode.Position.X, targetZ: targetNode.Position.Z,
            width: 1000, height: 600, isOrthographic: false);
        var vp = view * proj;
        Assert.True(Plant3DSceneData.TryProjectToScreen(targetNode.Position + new Vector3(0f, 1.2f, 0f), vp, 1000, 600, out var screenPt, out _));
        var ray = Plant3DSceneData.UnprojectRay(screenPt, vp, 1000, 600);

        const double testSimTime = 4.8;
        var expectedPose = Plant3DSceneData.ComputeLoaderPose(testSimTime, active: true);
        var expectedFlows = Plant3DSceneData.EvaluateProcessFlows(eqMap, testSimTime);

        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            var meshes = Plant3DSceneData.LoadAllGlbMeshes();
            Assert.Equal(16, meshes.Count);

            for (int iter = 0; iter < 10; iter++)
            {
                var flows = Plant3DSceneData.EvaluateProcessFlows(eqMap, testSimTime);
                var pose = Plant3DSceneData.ComputeLoaderPose(testSimTime, active: true);
                var picked = Plant3DSceneData.PickEquipment(ray, meshes, pose.ChassisPosition);

                Assert.Equal(expectedPose, pose);
                Assert.Equal("SS-101", picked);
                Assert.Equal(expectedFlows.Count, flows.Count);
                foreach (var (nodeId, expectedFlow) in expectedFlows)
                {
                    Assert.Equal(expectedFlow, flows[nodeId]);
                }

                var vib = Plant3DSceneData.ComputeScreenVibrationOffset("SS-101", testSimTime, 75.0);
                var expectedVib = Plant3DSceneData.ComputeScreenVibrationOffset("SS-101", testSimTime, 75.0);
                Assert.Equal(expectedVib, vib);
                Assert.True(float.IsFinite(vib.X) && float.IsFinite(vib.Y) && float.IsFinite(vib.Z));
            }

            return meshes.Count;
        })).ToArray();

        int[] counts = await Task.WhenAll(tasks);
        Assert.All(counts, c => Assert.Equal(16, c));
    }
}
