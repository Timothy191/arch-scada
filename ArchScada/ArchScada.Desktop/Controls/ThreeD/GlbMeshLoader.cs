using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace ArchScada.Desktop.Controls.ThreeD;

/// <summary>
/// Loaded 3D mesh data from a binary glTF 2.0 (.glb) file, flattened into model space
/// with per-vertex position, normal, PBR base color, metallic, and roughness.
/// </summary>
public sealed class GlbMeshData
{
    public const int FloatsPerVertex = 11; // pos(3) + normal(3) + rgb(3) + metallic(1) + roughness(1)

    public string Name { get; }
    public float[] Vertices { get; }
    public uint[] Indices { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public int VertexCount => Vertices.Length / FloatsPerVertex;
    public int TriangleCount => Indices.Length / 3;

    public GlbMeshData(string name, float[] vertices, uint[] indices, Vector3 boundsMin, Vector3 boundsMax)
    {
        Name = name;
        Vertices = vertices;
        Indices = indices;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }
}

/// <summary>
/// Zero-dependency binary glTF 2.0 (.glb) loader supporting Blender-exported meshes,
/// node hierarchy transforms, indexed/non-indexed triangles, and PBR material factors.
/// </summary>
public static class GlbMeshLoader
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint ChunkJson = 0x4E4F534A; // "JSON"
    private const uint ChunkBin = 0x004E4942;  // "BIN\0"

    public static GlbMeshData? Load(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var bytes = File.ReadAllBytes(filePath);
        return Parse(Path.GetFileNameWithoutExtension(filePath), bytes);
    }

    public static GlbMeshData? Parse(string name, ReadOnlySpan<byte> data)
    {
        if (data.Length < 20)
            return null;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (magic != GlbMagic || version != 2)
            return null;

        ReadOnlySpan<byte> jsonChunk = default;
        ReadOnlySpan<byte> binChunk = default;

        int offset = 12;
        while (offset + 8 <= data.Length)
        {
            int chunkLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            offset += 8;
            if (chunkLen < 0 || offset + chunkLen > data.Length)
                break;

            if (chunkType == ChunkJson)
                jsonChunk = data.Slice(offset, chunkLen);
            else if (chunkType == ChunkBin)
                binChunk = data.Slice(offset, chunkLen);

            offset += chunkLen;
        }

        if (jsonChunk.IsEmpty || binChunk.IsEmpty)
            return null;

        using var doc = JsonDocument.Parse(jsonChunk.ToArray());
        var root = doc.RootElement;

        if (!root.TryGetProperty("meshes", out var meshesEl) ||
            !root.TryGetProperty("accessors", out var accessorsEl) ||
            !root.TryGetProperty("bufferViews", out var bufferViewsEl))
        {
            return null;
        }

        var materials = ParseMaterials(root);
        var verts = new List<float>(4096);
        var indices = new List<uint>(2048);
        var bMin = new Vector3(float.PositiveInfinity);
        var bMax = new Vector3(float.NegativeInfinity);

        if (root.TryGetProperty("nodes", out var nodesEl) && nodesEl.GetArrayLength() > 0)
        {
            var rootNodeIndices = GetSceneRootNodes(root, nodesEl.GetArrayLength());
            foreach (int nodeIdx in rootNodeIndices)
            {
                TraverseNode(nodeIdx, Matrix4x4.Identity, nodesEl, meshesEl, accessorsEl, bufferViewsEl, binChunk, materials, verts, indices, ref bMin, ref bMax);
            }
        }
        else
        {
            for (int m = 0; m < meshesEl.GetArrayLength(); m++)
            {
                AppendMesh(meshesEl[m], Matrix4x4.Identity, accessorsEl, bufferViewsEl, binChunk, materials, verts, indices, ref bMin, ref bMax);
            }
        }

        if (verts.Count == 0 || indices.Count == 0)
            return null;

        return new GlbMeshData(name, verts.ToArray(), indices.ToArray(), bMin, bMax);
    }

    private readonly record struct MaterialInfo(Vector3 Color, float Metallic, float Roughness);

    private static List<MaterialInfo> ParseMaterials(JsonElement root)
    {
        var list = new List<MaterialInfo>();
        if (!root.TryGetProperty("materials", out var matsEl))
            return list;

        foreach (var mat in matsEl.EnumerateArray())
        {
            var color = new Vector3(0.28f, 0.34f, 0.44f);
            float metallic = 0.5f;
            float roughness = 0.45f;

            if (mat.TryGetProperty("pbrMetallicRoughness", out var pbr))
            {
                if (pbr.TryGetProperty("baseColorFactor", out var bcf) && bcf.GetArrayLength() >= 3)
                {
                    // glTF 2.0 baseColorFactor is linear-space RGB; store directly for physically based shading
                    color = new Vector3(
                        Math.Clamp(bcf[0].GetSingle(), 0f, 1f),
                        Math.Clamp(bcf[1].GetSingle(), 0f, 1f),
                        Math.Clamp(bcf[2].GetSingle(), 0f, 1f));
                }
                if (pbr.TryGetProperty("metallicFactor", out var mf))
                    metallic = Math.Clamp(mf.GetSingle(), 0f, 1f);
                if (pbr.TryGetProperty("roughnessFactor", out var rf))
                    roughness = Math.Clamp(rf.GetSingle(), 0.05f, 1f);
            }

            list.Add(new MaterialInfo(color, metallic, roughness));
        }

        return list;
    }

    private static List<int> GetSceneRootNodes(JsonElement root, int nodeCount)
    {
        var roots = new List<int>();
        if (root.TryGetProperty("scenes", out var scenesEl) && scenesEl.GetArrayLength() > 0)
        {
            int sceneIdx = root.TryGetProperty("scene", out var sEl) ? sEl.GetInt32() : 0;
            if (sceneIdx >= 0 && sceneIdx < scenesEl.GetArrayLength() &&
                scenesEl[sceneIdx].TryGetProperty("nodes", out var sceneNodes))
            {
                foreach (var n in sceneNodes.EnumerateArray())
                    roots.Add(n.GetInt32());
                return roots;
            }
        }

        for (int i = 0; i < nodeCount; i++)
            roots.Add(i);
        return roots;
    }

    private static void TraverseNode(
        int nodeIdx,
        Matrix4x4 parentTransform,
        JsonElement nodesEl,
        JsonElement meshesEl,
        JsonElement accessorsEl,
        JsonElement bufferViewsEl,
        ReadOnlySpan<byte> bin,
        List<MaterialInfo> materials,
        List<float> verts,
        List<uint> indices,
        ref Vector3 bMin,
        ref Vector3 bMax)
    {
        if (nodeIdx < 0 || nodeIdx >= nodesEl.GetArrayLength())
            return;

        var node = nodesEl[nodeIdx];
        var local = ReadNodeTransform(node);
        var world = local * parentTransform;

        if (node.TryGetProperty("mesh", out var meshIdxEl))
        {
            int meshIdx = meshIdxEl.GetInt32();
            if (meshIdx >= 0 && meshIdx < meshesEl.GetArrayLength())
            {
                AppendMesh(meshesEl[meshIdx], world, accessorsEl, bufferViewsEl, bin, materials, verts, indices, ref bMin, ref bMax);
            }
        }

        if (node.TryGetProperty("children", out var childrenEl))
        {
            foreach (var child in childrenEl.EnumerateArray())
            {
                TraverseNode(child.GetInt32(), world, nodesEl, meshesEl, accessorsEl, bufferViewsEl, bin, materials, verts, indices, ref bMin, ref bMax);
            }
        }
    }

    private static Matrix4x4 ReadNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var mEl) && mEl.GetArrayLength() == 16)
        {
            // glTF stores column-major; System.Numerics.Matrix4x4 is row-major (row-vector convention)
            return new Matrix4x4(
                mEl[0].GetSingle(), mEl[1].GetSingle(), mEl[2].GetSingle(), mEl[3].GetSingle(),
                mEl[4].GetSingle(), mEl[5].GetSingle(), mEl[6].GetSingle(), mEl[7].GetSingle(),
                mEl[8].GetSingle(), mEl[9].GetSingle(), mEl[10].GetSingle(), mEl[11].GetSingle(),
                mEl[12].GetSingle(), mEl[13].GetSingle(), mEl[14].GetSingle(), mEl[15].GetSingle());
        }

        var scale = Vector3.One;
        var rot = Quaternion.Identity;
        var trans = Vector3.Zero;

        if (node.TryGetProperty("scale", out var sEl) && sEl.GetArrayLength() == 3)
            scale = new Vector3(sEl[0].GetSingle(), sEl[1].GetSingle(), sEl[2].GetSingle());
        if (node.TryGetProperty("rotation", out var rEl) && rEl.GetArrayLength() == 4)
            rot = new Quaternion(rEl[0].GetSingle(), rEl[1].GetSingle(), rEl[2].GetSingle(), rEl[3].GetSingle());
        if (node.TryGetProperty("translation", out var tEl) && tEl.GetArrayLength() == 3)
            trans = new Vector3(tEl[0].GetSingle(), tEl[1].GetSingle(), tEl[2].GetSingle());

        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(trans);
    }

    private static void AppendMesh(
        JsonElement meshEl,
        Matrix4x4 world,
        JsonElement accessorsEl,
        JsonElement bufferViewsEl,
        ReadOnlySpan<byte> bin,
        List<MaterialInfo> materials,
        List<float> verts,
        List<uint> indices,
        ref Vector3 bMin,
        ref Vector3 bMax)
    {
        if (!meshEl.TryGetProperty("primitives", out var primsEl))
            return;

        Matrix4x4.Invert(world, out var invWorld);
        var normalMatrix = Matrix4x4.Transpose(invWorld);

        foreach (var prim in primsEl.EnumerateArray())
        {
            if (prim.TryGetProperty("mode", out var modeEl) && modeEl.GetInt32() != 4)
                continue; // only TRIANGLES

            if (!prim.TryGetProperty("attributes", out var attrsEl) ||
                !attrsEl.TryGetProperty("POSITION", out var posAccEl))
                continue;

            var positions = ReadVec3Accessor(posAccEl.GetInt32(), accessorsEl, bufferViewsEl, bin);
            if (positions.Length == 0)
                continue;

            Vector3[]? normals = null;
            if (attrsEl.TryGetProperty("NORMAL", out var normAccEl))
                normals = ReadVec3Accessor(normAccEl.GetInt32(), accessorsEl, bufferViewsEl, bin);

            var matInfo = new MaterialInfo(new Vector3(0.28f, 0.34f, 0.44f), 0.5f, 0.45f);
            if (prim.TryGetProperty("material", out var matIdxEl))
            {
                int mi = matIdxEl.GetInt32();
                if (mi >= 0 && mi < materials.Count)
                    matInfo = materials[mi];
            }

            uint baseVertex = (uint)(verts.Count / GlbMeshData.FloatsPerVertex);

            for (int i = 0; i < positions.Length; i++)
            {
                var p = Vector3.Transform(positions[i], world);
                var n = normals is not null && i < normals.Length
                    ? Vector3.Normalize(Vector3.TransformNormal(normals[i], normalMatrix))
                    : Vector3.UnitY;
                if (float.IsNaN(n.X)) n = Vector3.UnitY;

                bMin = Vector3.Min(bMin, p);
                bMax = Vector3.Max(bMax, p);

                verts.Add(p.X);
                verts.Add(p.Y);
                verts.Add(p.Z);
                verts.Add(n.X);
                verts.Add(n.Y);
                verts.Add(n.Z);
                verts.Add(matInfo.Color.X);
                verts.Add(matInfo.Color.Y);
                verts.Add(matInfo.Color.Z);
                verts.Add(matInfo.Metallic);
                verts.Add(matInfo.Roughness);
            }

            if (prim.TryGetProperty("indices", out var idxAccEl))
            {
                var rawIdx = ReadIndexAccessor(idxAccEl.GetInt32(), accessorsEl, bufferViewsEl, bin);
                for (int i = 0; i < rawIdx.Length; i++)
                    indices.Add(baseVertex + rawIdx[i]);
            }
            else
            {
                for (uint i = 0; i < positions.Length; i++)
                    indices.Add(baseVertex + i);
            }
        }
    }

    private static Vector3[] ReadVec3Accessor(int accessorIdx, JsonElement accessorsEl, JsonElement bufferViewsEl, ReadOnlySpan<byte> bin)
    {
        var acc = accessorsEl[accessorIdx];
        int count = acc.GetProperty("count").GetInt32();
        int bvIdx = acc.GetProperty("bufferView").GetInt32();
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;

        var bv = bufferViewsEl[bvIdx];
        int bvOffset = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        int stride = bv.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 12;
        if (stride < 12) stride = 12;

        var result = new Vector3[count];
        int start = bvOffset + accOffset;
        for (int i = 0; i < count; i++)
        {
            int pos = start + i * stride;
            if (pos + 12 > bin.Length) break;
            float x = BinaryPrimitives.ReadSingleLittleEndian(bin.Slice(pos, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(bin.Slice(pos + 4, 4));
            float z = BinaryPrimitives.ReadSingleLittleEndian(bin.Slice(pos + 8, 4));
            result[i] = new Vector3(x, y, z);
        }
        return result;
    }

    private static uint[] ReadIndexAccessor(int accessorIdx, JsonElement accessorsEl, JsonElement bufferViewsEl, ReadOnlySpan<byte> bin)
    {
        var acc = accessorsEl[accessorIdx];
        int count = acc.GetProperty("count").GetInt32();
        int compType = acc.GetProperty("componentType").GetInt32();
        int bvIdx = acc.GetProperty("bufferView").GetInt32();
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;

        var bv = bufferViewsEl[bvIdx];
        int bvOffset = bv.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        int elemSize = compType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            _ => 2
        };
        int stride = bv.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : elemSize;
        if (stride < elemSize) stride = elemSize;

        var result = new uint[count];
        int start = bvOffset + accOffset;
        for (int i = 0; i < count; i++)
        {
            int pos = start + i * stride;
            if (pos + elemSize > bin.Length) break;
            result[i] = compType switch
            {
                5121 => bin[pos],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(bin.Slice(pos, 2)),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(bin.Slice(pos, 4)),
                _ => 0
            };
        }
        return result;
    }
}
