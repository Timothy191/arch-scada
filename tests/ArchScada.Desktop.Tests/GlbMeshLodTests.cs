using System;
using System.Numerics;
using ArchScada.Desktop.Controls.ThreeD;
using Xunit;

namespace ArchScada.Desktop.Tests;

public class GlbMeshLodTests
{
    private const string VesselMesh = "magnetite_tank_tk301";

    [Fact]
    public void CreateLODLevel_ShrinksToBudgetAndRemapsIndices()
    {
        var source = BuildMesh($"{VesselMesh}.glb", triangles: 2000);

        var lod3 = GlbMeshLoader.CreateLODLevel(source, "LOD3");

        Assert.Equal("LOD3", lod3.LODTier);
        Assert.Equal(PlantMeshQuality.VesselBudgets.LOD3Background, lod3.LODVertexLimit);
        Assert.True(lod3.VertexCount <= lod3.LODVertexLimit,
            $"Expected {lod3.VertexCount} vertices to fit the {lod3.LODVertexLimit} budget.");
        Assert.True(lod3.VertexCount < source.VertexCount,
            "LOD3 must actually discard source vertices, not just triangles.");
        Assert.Equal(0, lod3.Indices.Length % 3);
        Assert.Equal(lod3.VertexCount * GlbMeshData.FloatsPerVertex, lod3.Vertices.Length);
        Assert.All(lod3.Indices, index => Assert.True(index < lod3.VertexCount));
    }

    [Fact]
    public void CreateLODLevel_PassesThroughWithinBudget()
    {
        var source = BuildMesh($"{VesselMesh}.glb", triangles: 2000);

        var lod1 = GlbMeshLoader.CreateLODLevel(source, "LOD1");

        Assert.Equal(PlantMeshQuality.VesselBudgets.LOD1Hero, lod1.LODVertexLimit);
        Assert.Same(source.Vertices, lod1.Vertices);
        Assert.Same(source.Indices, lod1.Indices);
        Assert.Equal(source.VertexCount, lod1.VertexCount);
    }

    [Fact]
    public void CreateLODLevel_RecomputesBoundsFromSurvivingVertices()
    {
        var source = BuildMesh($"{VesselMesh}.glb", triangles: 2000);

        var lod3 = GlbMeshLoader.CreateLODLevel(source, "LOD3");

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < lod3.VertexCount; i++)
        {
            int offset = i * GlbMeshData.FloatsPerVertex;
            var p = new Vector3(lod3.Vertices[offset], lod3.Vertices[offset + 1], lod3.Vertices[offset + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Assert.Equal(min, lod3.BoundsMin);
        Assert.Equal(max, lod3.BoundsMax);
    }

    [Theory]
    [InlineData("magnetite_tank_tk301.glb")]
    [InlineData("magnetite_tank_tk301")]
    [InlineData("magnetite_tank_tk301_LOD3")]
    public void GetMeshBudget_ResolvesCategoryRegardlessOfExtensionOrTier(string name)
    {
        var budget = GlbMeshLoader.GetMeshBudget(name);

        Assert.Equal(PlantMeshQuality.VesselBudgets, budget);
        Assert.NotEqual(PlantMeshQuality.VertexBudgets, budget);
    }

    [Fact]
    public void GetMeshBudget_FallsBackToDefaultForUnknownMesh()
    {
        Assert.Equal(PlantMeshQuality.VertexBudgets, GlbMeshLoader.GetMeshBudget("unknown_mesh.glb"));
    }

    private static GlbMeshData BuildMesh(string name, int triangles)
    {
        const int floatsPerVertex = GlbMeshData.FloatsPerVertex;
        int vertexCount = triangles * 3;
        var vertices = new float[vertexCount * floatsPerVertex];
        var indices = new uint[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            int offset = i * floatsPerVertex;
            vertices[offset] = i % 100;
            vertices[offset + 1] = i / 100;
            vertices[offset + 2] = 0f;

            vertices[offset + 3] = 0f;
            vertices[offset + 4] = 0f;
            vertices[offset + 5] = 1f;

            vertices[offset + 6] = 0.8f;
            vertices[offset + 7] = 0.4f;
            vertices[offset + 8] = 0.1f;
            vertices[offset + 9] = 0.2f;
            vertices[offset + 10] = 0.5f;

            indices[i] = (uint)i;
        }

        return new GlbMeshData(name, vertices, indices, Vector3.Zero, new Vector3(99f, 60f, 0f));
    }
}