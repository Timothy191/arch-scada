using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using StbImageSharp;

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

    // §5 LOD tier tracking per mesh.
    public string LODTier { get; }
    public int LODVertexLimit { get; }
    public bool UseDracoCompression { get; }

    public GlbMeshData(string name, float[] vertices, uint[] indices, Vector3 boundsMin, Vector3 boundsMax)
        : this(name, vertices, indices, boundsMin, boundsMax, "LOD3", 2500, false)
    {
    }

    public GlbMeshData(string name, float[] vertices, uint[] indices, Vector3 boundsMin, Vector3 boundsMax,
        string lodTier, int lodVertexLimit, bool useDraco)
    {
        Name = name;
        Vertices = vertices;
        Indices = indices;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        LODTier = lodTier;
        LODVertexLimit = lodVertexLimit;
        UseDracoCompression = useDraco;
    }
}

/// <summary>
/// glTF 2.0 (.glb) asset loader backed by SharpGLTF.Core and StbImageSharp, supporting full
/// scene hierarchies, world transforms, indexed/non-indexed triangles, PBR factors, and embedded textures.
/// </summary>
public static class GlbMeshLoader
{
    private const uint GlbMagic = 0x46546C67; // "glTF"

    // Maps a GLB base name (no extension) to its equipment category for §5 vertex-budget enforcement.
    private static readonly Dictionary<string, string> MeshCategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wheel_loader_chassis"] = "WheelLoader",
        ["vibro_feeder_vf101"] = "VibratingGrizzlyFeeder",
        ["jaw_crusher_jc101"] = "JawCrusher",
        ["double_roll_crusher_drc101"] = "DoubleRollCrusher",
        ["central_sizing_screen_ss101"] = "SizingScreen",
        ["surge_bin_bn201"] = "SurgeBin",
        ["feeder_bin_bn101"] = "ReceivingHopper",
        ["wash_plant_module_wp301"] = "WashPlantModule",
        ["magnetite_tank_tk301"] = "MediumTank",
        ["thickener_tk201"] = "Thickener",
        ["centrifugal_pump"] = "CentrifugalPump",
        ["stockpile_cone"] = "Stockpile",
    };

    /// <summary>§5 Gets the vertex budget for a mesh based on its equipment category.</summary>
    public static LodVertexBudgets GetMeshBudget(string glbFileName)
    {
        return MeshCategoryMap.TryGetValue(NormalizeMeshKey(glbFileName), out var category)
            ? PlantMeshQuality.GetEquipmentBudget(category)
            : PlantMeshQuality.VertexBudgets;
    }

    /// <summary>
    /// Normalizes a mesh identifier to its category key. <see cref="GlbMeshData.Name"/> is set from
    /// <see cref="Path.GetFileNameWithoutExtension(string)"/> on the load path, while callers often
    /// pass the full file name; LOD chains also append a tier suffix.
    /// </summary>
    private static string NormalizeMeshKey(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var key = name;
        if (key.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            key = key[..^4];

        foreach (var suffix in new[] { "_LOD1", "_LOD2", "_LOD3" })
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^suffix.Length];
                break;
            }
        }

        return key;
    }

    /// <summary>
    /// §5 Creates a coarser LOD that actually satisfies the category vertex budget: whole triangles
    /// are sampled by stride, then the retained geometry is compacted so only referenced vertices
    /// survive. Bounds are recomputed from the surviving vertices. Sources already within budget are
    /// passed through unchanged.
    /// </summary>
    public static GlbMeshData CreateLODLevel(GlbMeshData source, string lodTier)
    {
        ArgumentNullException.ThrowIfNull(source);

        var budget = GetMeshBudget(source.Name);
        int targetVertices = lodTier switch
        {
            "LOD1" => budget.LOD1Hero,
            "LOD2" => budget.LOD2Standard,
            _ => budget.LOD3Background
        };

        if (source.VertexCount <= targetVertices || source.TriangleCount == 0)
        {
            return new GlbMeshData($"{source.Name}_{lodTier}", source.Vertices, source.Indices,
                source.BoundsMin, source.BoundsMax, lodTier, targetVertices, source.UseDracoCompression);
        }

        int triangleCount = source.TriangleCount;
        // Closed manifold meshes have roughly two triangles per vertex, so this is a good first guess.
        int stride = Math.Max(1, (int)Math.Ceiling(triangleCount / (2.0 * targetVertices)));

        DecimationResult result;
        while (true)
        {
            result = DecimateAndCompact(source, stride);
            if (result.VertexCount <= targetVertices || stride >= triangleCount)
                break;
            stride = Math.Min(triangleCount, Math.Max(stride + 1, (int)Math.Ceiling(stride * 1.5)));
        }

        return new GlbMeshData($"{source.Name}_{lodTier}", result.Vertices, result.Indices,
            result.BoundsMin, result.BoundsMax, lodTier, targetVertices, source.UseDracoCompression);
    }

    private readonly record struct DecimationResult(
        float[] Vertices, uint[] Indices, Vector3 BoundsMin, Vector3 BoundsMax)
    {
        public int VertexCount => Vertices.Length / GlbMeshData.FloatsPerVertex;
    }

    /// <summary>Keeps every <paramref name="stride"/>-th triangle and remaps its vertices into a packed buffer.</summary>
    private static DecimationResult DecimateAndCompact(GlbMeshData source, int stride)
    {
        const int floatsPerVertex = GlbMeshData.FloatsPerVertex;
        int sourceVertexCount = source.VertexCount;
        var remap = new int[sourceVertexCount];
        Array.Fill(remap, -1);

        var vertices = new List<float>(1024 * floatsPerVertex);
        var indices = new List<uint>(source.Indices.Length / Math.Max(1, stride));
        var boundsMin = new Vector3(float.PositiveInfinity);
        var boundsMax = new Vector3(float.NegativeInfinity);

        for (int triangle = 0; triangle < source.TriangleCount; triangle += stride)
        {
            int baseIndex = triangle * 3;
            if (baseIndex + 2 >= source.Indices.Length)
                break;

            for (int corner = 0; corner < 3; corner++)
            {
                uint oldIndex = source.Indices[baseIndex + corner];
                if (oldIndex >= sourceVertexCount)
                    continue;

                int mapped = remap[oldIndex];
                if (mapped < 0)
                {
                    mapped = vertices.Count / floatsPerVertex;
                    remap[oldIndex] = mapped;

                    int sourceOffset = (int)oldIndex * floatsPerVertex;
                    for (int i = 0; i < floatsPerVertex; i++)
                        vertices.Add(source.Vertices[sourceOffset + i]);

                    var position = new Vector3(
                        source.Vertices[sourceOffset],
                        source.Vertices[sourceOffset + 1],
                        source.Vertices[sourceOffset + 2]);
                    boundsMin = Vector3.Min(boundsMin, position);
                    boundsMax = Vector3.Max(boundsMax, position);
                }

                indices.Add((uint)mapped);
            }
        }

        if (vertices.Count == 0)
            return new DecimationResult(source.Vertices, source.Indices, source.BoundsMin, source.BoundsMax);

        return new DecimationResult(vertices.ToArray(), indices.ToArray(), boundsMin, boundsMax);
    }

    public static GlbMeshData? Load(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var bytes = File.ReadAllBytes(filePath);
        return ParseOwnedBytes(Path.GetFileNameWithoutExtension(filePath), bytes);
    }

    public static GlbMeshData? Parse(string name, ReadOnlySpan<byte> data)
    {
        if (data.Length < 20)
            return null;

        return ParseOwnedBytes(name, data.ToArray());
    }

    private static GlbMeshData? ParseOwnedBytes(string name, byte[] bytes)
    {
        if (bytes.Length < 20)
            return null;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        if (magic != GlbMagic || version != 2)
            return null;

        ModelRoot model;
        try
        {
            model = ModelRoot.ParseGLB(
                new ArraySegment<byte>(bytes),
                new ReadSettings { Validation = ValidationMode.Skip });
        }
        catch
        {
            return null;
        }

        var verts = new List<float>(4096);
        var indices = new List<uint>(2048);
        var decodedImages = new Dictionary<int, ImageResult?>();
        var bMin = new Vector3(float.PositiveInfinity);
        var bMax = new Vector3(float.NegativeInfinity);

        if (model.DefaultScene is not null)
        {
            foreach (var rootNode in model.DefaultScene.VisualChildren)
            {
                TraverseNode(rootNode, verts, indices, decodedImages, ref bMin, ref bMax);
            }
        }
        else if (model.LogicalScenes.Count > 0)
        {
            foreach (var rootNode in model.LogicalScenes[0].VisualChildren)
            {
                TraverseNode(rootNode, verts, indices, decodedImages, ref bMin, ref bMax);
            }
        }
        else
        {
            foreach (var mesh in model.LogicalMeshes)
            {
                AppendMesh(mesh, Matrix4x4.Identity, verts, indices, decodedImages, ref bMin, ref bMax);
            }
        }

        if (verts.Count == 0 || indices.Count == 0)
            return null;

        return new GlbMeshData(name, verts.ToArray(), indices.ToArray(), bMin, bMax);
    }

    private static void TraverseNode(
        Node node,
        List<float> verts,
        List<uint> indices,
        Dictionary<int, ImageResult?> decodedImages,
        ref Vector3 bMin,
        ref Vector3 bMax)
    {
        if (node.Mesh is not null)
        {
            AppendMesh(node.Mesh, node.WorldMatrix, verts, indices, decodedImages, ref bMin, ref bMax);
        }

        foreach (var child in node.VisualChildren)
        {
            TraverseNode(child, verts, indices, decodedImages, ref bMin, ref bMax);
        }
    }

    private static void AppendMesh(
        Mesh mesh,
        Matrix4x4 world,
        List<float> verts,
        List<uint> indices,
        Dictionary<int, ImageResult?> decodedImages,
        ref Vector3 bMin,
        ref Vector3 bMax)
    {
        if (!Matrix4x4.Invert(world, out var invWorld))
            invWorld = Matrix4x4.Identity;
        var normalMatrix = Matrix4x4.Transpose(invWorld);

        foreach (var prim in mesh.Primitives)
        {
            if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                continue;

            var posAcc = prim.GetVertexAccessor("POSITION");
            if (posAcc is null || posAcc.Count <= 0)
                continue;

            var positions = posAcc.AsVector3Array();
            var normAcc = prim.GetVertexAccessor("NORMAL");
            var normals = normAcc?.AsVector3Array();
            var uvAcc = prim.GetVertexAccessor("TEXCOORD_0");
            var uvs = uvAcc?.AsVector2Array();

            var matInfo = ExtractMaterialInfo(prim.Material, decodedImages);

            uint baseVertex = (uint)(verts.Count / GlbMeshData.FloatsPerVertex);
            int vertCount = positions.Count;
            verts.EnsureCapacity(verts.Count + vertCount * GlbMeshData.FloatsPerVertex);

            for (int i = 0; i < vertCount; i++)
            {
                var p = Vector3.Transform(positions[i], world);
                var n = normals is not null && i < normals.Count
                    ? Vector3.Normalize(Vector3.TransformNormal(normals[i], normalMatrix))
                    : Vector3.UnitY;
                if (float.IsNaN(n.X) || float.IsInfinity(n.X))
                    n = Vector3.UnitY;

                var color = matInfo.Color;
                if (matInfo.BaseColorImage is not null && uvs is not null && i < uvs.Count)
                {
                    color *= SampleLinearRgb(matInfo.BaseColorImage, uvs[i]);
                }

                bMin = Vector3.Min(bMin, p);
                bMax = Vector3.Max(bMax, p);

                verts.Add(p.X);
                verts.Add(p.Y);
                verts.Add(p.Z);
                verts.Add(n.X);
                verts.Add(n.Y);
                verts.Add(n.Z);
                verts.Add(color.X);
                verts.Add(color.Y);
                verts.Add(color.Z);
                verts.Add(matInfo.Metallic);
                verts.Add(matInfo.Roughness);
            }

            var rawIndices = prim.GetIndices();
            if (rawIndices is not null && rawIndices.Count > 0)
            {
                indices.EnsureCapacity(indices.Count + rawIndices.Count);
                for (int i = 0; i < rawIndices.Count; i++)
                    indices.Add(baseVertex + rawIndices[i]);
            }
            else
            {
                indices.EnsureCapacity(indices.Count + vertCount);
                for (uint i = 0; i < (uint)vertCount; i++)
                    indices.Add(baseVertex + i);
            }
        }
    }

    private readonly record struct MaterialInfo(
        Vector3 Color,
        float Metallic,
        float Roughness,
        ImageResult? BaseColorImage);

    private static MaterialInfo ExtractMaterialInfo(Material? material, Dictionary<int, ImageResult?> decodedImages)
    {
        var color = new Vector3(0.28f, 0.34f, 0.44f);
        float metallic = 0.5f;
        float roughness = 0.45f;
        ImageResult? baseImage = null;

        if (material is null)
            return new MaterialInfo(color, metallic, roughness, baseImage);

        if (material.TryGetChannel("BaseColor", out var baseCh))
        {
            foreach (var mp in baseCh.Parameters)
            {
                if (mp.Value is Vector4 v4)
                {
                    color = new Vector3(
                        Math.Clamp(v4.X, 0f, 1f),
                        Math.Clamp(v4.Y, 0f, 1f),
                        Math.Clamp(v4.Z, 0f, 1f));
                    break;
                }
            }

            var img = baseCh.Texture?.PrimaryImage;
            if (img is not null)
            {
                baseImage = GetOrDecodeImage(img, decodedImages);
            }
        }

        if (material.TryGetChannel("MetallicRoughness", out var mrCh))
        {
            foreach (var mp in mrCh.Parameters)
            {
                if (mp.Value is float fVal)
                {
                    if (string.Equals(mp.Name, "MetallicFactor", StringComparison.OrdinalIgnoreCase))
                        metallic = Math.Clamp(fVal, 0f, 1f);
                    else if (string.Equals(mp.Name, "RoughnessFactor", StringComparison.OrdinalIgnoreCase))
                        roughness = Math.Clamp(fVal, 0.05f, 1f);
                }
            }
        }

        return new MaterialInfo(color, metallic, roughness, baseImage);
    }

    private static ImageResult? GetOrDecodeImage(Image img, Dictionary<int, ImageResult?> cache)
    {
        if (cache.TryGetValue(img.LogicalIndex, out var cached))
            return cached;

        ImageResult? decoded = null;
        try
        {
            var mem = img.Content.Content;
            if (!mem.IsEmpty)
            {
                decoded = ImageResult.FromMemory(mem.ToArray(), ColorComponents.RedGreenBlueAlpha);
            }
        }
        catch
        {
            decoded = null;
        }

        cache[img.LogicalIndex] = decoded;
        return decoded;
    }

    private static Vector3 SampleLinearRgb(ImageResult image, Vector2 uv)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Data is null || image.Data.Length < 4)
            return Vector3.One;

        float u = uv.X - MathF.Floor(uv.X);
        float v = uv.Y - MathF.Floor(uv.Y);
        int x = Math.Clamp((int)(u * image.Width), 0, image.Width - 1);
        int y = Math.Clamp((int)(v * image.Height), 0, image.Height - 1);
        int idx = (y * image.Width + x) * 4;
        if (idx + 2 >= image.Data.Length)
            return Vector3.One;

        float r = MathF.Pow(image.Data[idx] / 255f, 2.2f);
        float g = MathF.Pow(image.Data[idx + 1] / 255f, 2.2f);
        float b = MathF.Pow(image.Data[idx + 2] / 255f, 2.2f);
        return new Vector3(r, g, b);
    }
}
