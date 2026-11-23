using System;
using System.Linq;
using System.Numerics;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Tests;

public class Plant3DTests
{
    [Fact]
    public void GlbMeshLoader_LoadsAllElevenExportedChppModelsWithValidGeometry()
    {
        Plant3DSceneData.InvalidateMeshCache();
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();

        Assert.Equal(11, meshes.Count);
        foreach (var (name, mesh) in meshes)
        {
            Assert.True(mesh.VertexCount > 24, $"{name} should have multi-part vertex geometry");
            Assert.True(mesh.TriangleCount > 12, $"{name} should have valid triangles");
            Assert.True(mesh.BoundsMax.Y > mesh.BoundsMin.Y, $"{name} should have positive height");
        }
    }

    [Fact]
    public void Plant3DSceneData_CoversAllFourteenEquipmentPlacementsWithGlbMeshes()
    {
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();
        Assert.Equal(14, Plant3DSceneData.Placements.Count);

        var ids = Plant3DSceneData.Placements.Select(p => p.Id).ToHashSet();
        Assert.Contains("PP-202", ids);
        Assert.Contains("PP-203", ids);
        Assert.Contains("SC-102", ids);
        Assert.Contains("TK-301", ids);
        Assert.Contains("MS-301", ids);
        Assert.Contains("DM-301", ids);

        foreach (var p in Plant3DSceneData.Placements)
        {
            Assert.True(meshes.ContainsKey(p.GlbFileName), $"Missing loaded GLB '{p.GlbFileName}' for {p.Id}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plant3DSceneData_RaycastPicking_SelectsThickenerWhenProjectingCenter(bool isOrthographic)
    {
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();
        var tk = Plant3DSceneData.Placements.First(p => p.Id == "TK-201");

        var (view, proj, _) = Plant3DSceneData.BuildCameraMatrices(
            yaw: -0.62, pitch: 0.52, zoom: 1.05, targetX: 0.0, targetZ: 0.5,
            width: 800, height: 420, isOrthographic: isOrthographic);
        var vp = view * proj;

        var worldPoint = tk.Position + new Vector3(0f, 0.9f, 0f);
        bool projected = Plant3DSceneData.TryProjectToScreen(worldPoint, vp, 800, 420, out var screenPt, out _);
        Assert.True(projected);

        var ray = Plant3DSceneData.UnprojectRay(screenPt, vp, 800, 420);
        var picked = Plant3DSceneData.PickEquipment(ray, meshes);

        Assert.Equal("TK-201", picked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plant3DSceneData_BuildCameraMatrices_MapsNearAndFarToOpenGlClipSpace(bool isOrthographic)
    {
        const double targetX = 0.0;
        const double targetZ = 0.5;
        var (view, proj, camPos) = Plant3DSceneData.BuildCameraMatrices(
            yaw: -0.62, pitch: 0.52, zoom: 1.05, targetX: targetX, targetZ: targetZ,
            width: 800, height: 420, isOrthographic: isOrthographic);

        var target = new Vector3((float)targetX, 0.9f, (float)targetZ);
        var forward = Vector3.Normalize(target - camPos);
        float near = isOrthographic ? 0.1f : 0.2f;
        const float far = 120f;

        var vp = view * proj;
        var nearClip = Vector4.Transform(new Vector4(camPos + forward * near, 1f), vp);
        var farClip = Vector4.Transform(new Vector4(camPos + forward * far, 1f), vp);

        float nearNdcZ = nearClip.Z / nearClip.W;
        float farNdcZ = farClip.Z / farClip.W;

        Assert.InRange(nearNdcZ, -1f - 1e-4f, -1f + 1e-4f);
        Assert.InRange(farNdcZ, 1f - 1e-4f, 1f + 1e-4f);
    }

    [Fact]
    public void Plant3DSceneData_FlowSpeed_StopsWhenSourceStoppedOrFaulted()
    {
        var stopped = new EquipmentItem { Id = "CV-101", Status = EquipmentStatus.Stopped, LoadPercent = 80 };
        var faulted = new EquipmentItem { Id = "CV-101", Status = EquipmentStatus.Fault, LoadPercent = 80 };
        var runningLow = new EquipmentItem { Id = "CV-101", Status = EquipmentStatus.Running, LoadPercent = 20 };
        var runningHigh = new EquipmentItem { Id = "CV-101", Status = EquipmentStatus.Running, LoadPercent = 90 };

        Assert.Equal(0.0, Plant3DSceneData.GetFlowSpeedFactor(stopped));
        Assert.Equal(0.0, Plant3DSceneData.GetFlowSpeedFactor(faulted));
        Assert.True(Plant3DSceneData.GetFlowSpeedFactor(runningHigh) > Plant3DSceneData.GetFlowSpeedFactor(runningLow));
    }

    [Fact]
    public void Plant3DSceneData_SamplePolyline_InterpolatesStartMidEnd()
    {
        var pts = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 0, 10) };

        Assert.Equal(new Vector3(0, 0, 0), Plant3DSceneData.SamplePolyline(pts, 0f));
        Assert.Equal(new Vector3(10, 0, 0), Plant3DSceneData.SamplePolyline(pts, 0.5f));
        Assert.Equal(new Vector3(10, 0, 10), Plant3DSceneData.SamplePolyline(pts, 1f));
    }
}
