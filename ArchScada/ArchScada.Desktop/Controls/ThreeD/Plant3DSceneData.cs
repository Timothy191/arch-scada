using System;
using System.Collections.Generic;
using System.IO;
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

public sealed record EquipmentPlacement(
    string Id,
    string GlbFileName,
    Vector3 Position,
    float YawDegrees,
    Vector3 Scale,
    float LabelHeight,
    Color DefaultColor,
    Vector3 HalfExtents,
    bool HasFillLevel = false,
    float FillRadius = 0f,
    float FillBaseY = 0f,
    float FillMaxHeight = 0f,
    bool Vibrates = false,
    bool HasRotatingTop = false,
    float RotatingTopY = 0f,
    float RotatingTopLength = 0f);

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

/// <summary>
/// Shared 3D CHPP plant scene definition, GLB asset cache, orthogonal pipe topology,
/// telemetry-driven animation helpers, and 3D raycast picking.
/// Used by both the hardware OpenGL viewport and the high-fidelity software fallback.
/// </summary>
public static class Plant3DSceneData
{
    public static readonly IReadOnlyList<EquipmentPlacement> Placements = new EquipmentPlacement[]
    {
        // Crushing & Screening Circuit
        new("CR-101", "crusher_cr101.glb",         new(-7.5f, 0f, -2.5f),   0f, new(1.0f, 1.0f, 1.0f), 3.7f, Color.Parse("#334155"), new(1.3f, 1.6f, 1.2f)),
        new("CV-101", "conveyor_segment.glb",      new(-4.4f, 0.1f, -2.5f), 0f, new(0.68f, 1.0f, 1.0f), 1.85f, Color.Parse("#1E3A5F"), new(2.1f, 0.6f, 0.6f)),
        new("SC-102", "vibrating_screen_sc102.glb",new(-1.3f, 0f, -2.5f),   0f, new(1.0f, 1.0f, 1.0f), 2.75f, Color.Parse("#0284C7"), new(1.1f, 1.1f, 1.0f), Vibrates: true),
        new("CV-102", "conveyor_segment.glb",      new(-1.3f, 0.1f, 0.6f), 90f, new(0.68f, 1.0f, 1.0f), 1.75f, Color.Parse("#1E3A5F"), new(0.6f, 0.6f, 2.1f)),

        // Thickener & Slurry Pumping Circuit
        new("TK-201", "thickener_tk201.glb",       new(1.2f, 0f, 3.2f),     0f, new(1.0f, 1.0f, 1.0f), 2.95f, Color.Parse("#1E40AF"), new(2.9f, 1.3f, 2.9f), HasFillLevel: true, FillRadius: 2.55f, FillBaseY: 0.25f, FillMaxHeight: 1.30f, HasRotatingTop: true, RotatingTopY: 1.92f, RotatingTopLength: 5.5f),
        new("PP-201", "centrifugal_pump.glb",      new(-2.4f, 0f, 4.2f),    0f, new(1.0f, 1.0f, 1.0f), 1.75f, Color.Parse("#0EA5E9"), new(0.8f, 0.7f, 0.6f)),
        new("PP-202", "centrifugal_pump.glb",      new(-2.4f, 0f, 5.6f),    0f, new(1.0f, 1.0f, 1.0f), 1.75f, Color.Parse("#475569"), new(0.8f, 0.7f, 0.6f)),
        new("PP-203", "centrifugal_pump.glb",      new(4.5f, 0f, 3.0f),   180f, new(0.95f, 0.95f, 0.95f), 1.70f, Color.Parse("#10B981"), new(0.8f, 0.7f, 0.6f)),

        // Magnetite Preparation Circuit
        new("TK-301", "magnetite_tank_tk301.glb",  new(5.8f, 0f, -2.2f),    0f, new(1.0f, 1.0f, 1.0f), 3.75f, Color.Parse("#B45309"), new(1.5f, 1.7f, 1.5f), HasFillLevel: true, FillRadius: 1.28f, FillBaseY: 0.22f, FillMaxHeight: 2.05f),
        new("MS-301", "magnetic_separator_ms301.glb", new(3.2f, 0f, -2.2f), 0f, new(1.0f, 1.0f, 1.0f), 2.55f, Color.Parse("#0D9488"), new(1.1f, 1.0f, 0.9f), HasRotatingTop: true, RotatingTopY: 1.38f, RotatingTopLength: 1.45f),
        new("DM-301", "dense_medium_sump_dm301.glb",  new(5.8f, 0f, 0.8f),  0f, new(1.0f, 1.0f, 1.0f), 2.55f, Color.Parse("#0284C7"), new(0.9f, 1.1f, 0.9f)),

        // Fine Coal Flotation, Dewatering & Silo Loadout
        new("FC-401", "flotation_column_fc401.glb",new(-5.8f, 0f, 3.6f),    0f, new(1.0f, 1.0f, 1.0f), 3.80f, Color.Parse("#0284C7"), new(1.2f, 1.7f, 1.2f), HasFillLevel: true, FillRadius: 0.76f, FillBaseY: 0.45f, FillMaxHeight: 2.25f),
        new("CF-501", "centrifuge_cf501.glb",      new(5.8f, 0f, 4.6f),     0f, new(1.0f, 1.0f, 1.0f), 2.25f, Color.Parse("#7C3AED"), new(1.2f, 0.9f, 0.8f), Vibrates: true),
        new("SL-601", "coal_silo_sl601.glb",       new(-8.2f, 0f, 1.6f),    0f, new(1.0f, 1.0f, 1.0f), 5.00f, Color.Parse("#059669"), new(1.5f, 2.3f, 1.5f), HasFillLevel: true, FillRadius: 1.18f, FillBaseY: 1.35f, FillMaxHeight: 2.25f)
    };

    public static readonly IReadOnlyList<FoundationPad> FoundationPads = new FoundationPad[]
    {
        new("CRUSHING & SCREENING", new(-4.4f, -0.06f, -2.5f), new(9.2f, 0.12f, 3.4f), Color.Parse("#F59E0B")),
        new("THICKENER & PUMPING",  new(1.0f, -0.06f, 4.1f),   new(8.8f, 0.12f, 5.2f), Color.Parse("#38BDF8")),
        new("MAGNETITE PREP",       new(4.6f, -0.06f, -0.8f),  new(5.4f, 0.12f, 5.4f), Color.Parse("#14B8A6")),
        new("FINE COAL & LOADOUT",  new(-7.0f, -0.06f, 2.6f),  new(5.2f, 0.12f, 4.6f), Color.Parse("#22C55E")),
        new("DEWATERING",           new(5.8f, -0.06f, 4.6f),   new(3.2f, 0.12f, 2.4f), Color.Parse("#A855F7"))
    };

    /// <summary>
    /// Orthogonal 3D process piping & conveyor material flow routes with 90-degree bends.
    /// </summary>
    public static readonly IReadOnlyList<PipeRoute> PipeRoutes = new PipeRoute[]
    {
        // CR-101 -> CV-101 -> SC-102 (ROM Coal)
        new("CV-101", FlowMedium.Coal, 0.08f, new Vector3[]
        {
            new(-6.4f, 0.98f, -2.5f),
            new(-2.4f, 0.98f, -2.5f)
        }),
        // SC-102 -> CV-102 -> SL-601 (Product Coal)
        new("CV-102", FlowMedium.Coal, 0.08f, new Vector3[]
        {
            new(-1.3f, 0.98f, -1.2f),
            new(-1.3f, 0.98f, 2.3f),
            new(-4.8f, 1.35f, 2.3f),
            new(-8.2f, 1.65f, 2.3f),
            new(-8.2f, 1.65f, 1.6f)
        }),
        // SC-102 -> FC-401 (Fine Coal Slurry Feed)
        new("SC-102", FlowMedium.Froth, 0.07f, new Vector3[]
        {
            new(-1.3f, 0.65f, -1.5f),
            new(-3.8f, 0.65f, -1.5f),
            new(-3.8f, 2.30f, 3.6f),
            new(-5.0f, 2.30f, 3.6f)
        }),
        // TK-201 -> PP-201 -> TK-301 (Underflow / Process Water Return)
        new("PP-201", FlowMedium.Slurry, 0.075f, new Vector3[]
        {
            new(1.2f, 0.48f, 3.2f),
            new(-2.0f, 0.48f, 3.2f),
            new(-2.0f, 0.48f, 4.2f),
            new(-2.0f, 1.15f, 4.2f),
            new(2.2f, 1.15f, 4.2f),
            new(2.2f, 1.15f, -0.4f),
            new(5.8f, 1.15f, -0.4f),
            new(5.8f, 1.15f, -2.2f)
        }),
        // MS-301 -> TK-301 -> DM-301 (Dense Medium Magnetite)
        new("MS-301", FlowMedium.Magnetite, 0.07f, new Vector3[]
        {
            new(3.2f, 1.05f, -2.2f),
            new(5.8f, 1.05f, -2.2f),
            new(5.8f, 1.05f, 0.8f)
        }),
        // DM-301 -> PP-203 -> CF-501 (Dewatering Feed)
        new("DM-301", FlowMedium.Slurry, 0.075f, new Vector3[]
        {
            new(5.8f, 0.55f, 0.8f),
            new(5.8f, 0.55f, 3.0f),
            new(4.5f, 0.55f, 3.0f),
            new(4.5f, 1.05f, 3.0f),
            new(4.5f, 1.05f, 4.6f),
            new(5.2f, 1.05f, 4.6f)
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

            _meshCache = new Dictionary<string, GlbMeshData>(StringComparer.OrdinalIgnoreCase);
            var exportDir = FindAssetsExportDirectory();
            if (exportDir is not null && Directory.Exists(exportDir))
            {
                foreach (var p in Placements)
                {
                    if (_meshCache.ContainsKey(p.GlbFileName))
                        continue;

                    var fullPath = Path.Combine(exportDir, p.GlbFileName);
                    var mesh = GlbMeshLoader.Load(fullPath);
                    if (mesh is not null)
                        _meshCache[p.GlbFileName] = mesh;
                }
            }

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
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
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

    public static Color GetStatusTint(EquipmentPlacement placement, EquipmentItem? eq, Unity3DEffectSettings fx)
    {
        if (eq is null) return placement.DefaultColor;
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
            _ => placement.DefaultColor
        };
    }

    public static Color GetMediumColor(FlowMedium medium) => medium switch
    {
        FlowMedium.Coal => Color.Parse("#F59E0B"),
        FlowMedium.Froth => Color.Parse("#22C55E"),
        FlowMedium.Magnetite => Color.Parse("#14B8A6"),
        _ => Color.Parse("#38BDF8")
    };

    /// <summary>
    /// Computes normalized flow speed (0 when Stopped/Fault, otherwise scaled by LoadPercent).
    /// </summary>
    public static double GetFlowSpeedFactor(EquipmentItem? source)
    {
        if (source is null) return 0.65;
        if (source.Status is EquipmentStatus.Stopped or EquipmentStatus.Fault or EquipmentStatus.Offline)
            return 0.0;
        return Math.Clamp(0.35 + (source.LoadPercent / 100.0) * 0.95, 0.35, 1.30);
    }

    /// <summary>
    /// Samples a point at normalized parameter t in [0,1] along a multi-waypoint polyline.
    /// </summary>
    public static Vector3 SamplePolyline(Vector3[] pts, float t)
    {
        if (pts.Length == 0) return Vector3.Zero;
        if (pts.Length == 1) return pts[0];

        float total = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
            total += Vector3.Distance(pts[i], pts[i + 1]);
        if (total < 1e-5f) return pts[0];

        float target = Math.Clamp(t, 0f, 1f) * total;
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
    /// Builds standard right-handed View & Projection matrices matching the orbit camera state.
    /// </summary>
    public static (Matrix4x4 View, Matrix4x4 Proj, Vector3 CameraPos) BuildCameraMatrices(
        double yaw, double pitch, double zoom, double targetX, double targetZ,
        double width, double height, bool isOrthographic)
    {
        float dist = (float)(19.0 / Math.Clamp(zoom, 0.4, 3.0));
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
            float orthoHalfH = (float)(9.5 / Math.Clamp(zoom, 0.4, 3.0));
            proj = Matrix4x4.CreateOrthographic(orthoHalfH * 2f * aspect, orthoHalfH * 2f, 0.1f, 120f);
        }
        else
        {
            proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.2f, aspect, 0.2f, 120f);
        }

        return (view, proj, camPos);
    }

    /// <summary>
    /// Projects a world-space point into 2D viewport pixel coordinates using the exact camera matrices.
    /// </summary>
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

    /// <summary>
    /// Unprojects a 2D viewport pixel into a world-space 3D picking ray.
    /// </summary>
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
    /// Performs exact Ray-AABB picking across all 14 plant equipment placements and returns the closest hit ID.
    /// </summary>
    public static string? PickEquipment(Ray3D ray, IReadOnlyDictionary<string, GlbMeshData>? meshes = null)
    {
        string? bestId = null;
        float bestT = float.PositiveInfinity;

        foreach (var p in Placements)
        {
            Vector3 min, max;
            if (meshes is not null && meshes.TryGetValue(p.GlbFileName, out var mesh))
            {
                var scaledMin = mesh.BoundsMin * p.Scale;
                var scaledMax = mesh.BoundsMax * p.Scale;
                // Conservative yaw-invariant XZ radius so rotated conveyors pick accurately
                float rx = MathF.Max(MathF.Abs(scaledMin.X), MathF.Abs(scaledMax.X));
                float rz = MathF.Max(MathF.Abs(scaledMin.Z), MathF.Abs(scaledMax.Z));
                bool rot90 = MathF.Abs(p.YawDegrees % 180f) > 45f;
                float hx = rot90 ? rz : rx;
                float hz = rot90 ? rx : rz;
                min = new Vector3(p.Position.X - hx, p.Position.Y + scaledMin.Y, p.Position.Z - hz);
                max = new Vector3(p.Position.X + hx, p.Position.Y + scaledMax.Y, p.Position.Z + hz);
            }
            else
            {
                min = new Vector3(p.Position.X - p.HalfExtents.X, p.Position.Y, p.Position.Z - p.HalfExtents.Z);
                max = new Vector3(p.Position.X + p.HalfExtents.X, p.Position.Y + p.HalfExtents.Y * 2f, p.Position.Z + p.HalfExtents.Z);
            }

            if (RayIntersectsAabb(ray, min, max, out float t) && t < bestT)
            {
                bestT = t;
                bestId = p.Id;
            }
        }

        return bestId;
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
