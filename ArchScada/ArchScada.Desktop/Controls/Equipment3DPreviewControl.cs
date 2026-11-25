using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Live 3D turntable preview canvas rendered under each equipment card.
/// Loads the actual glTF 2.0 (.glb) mesh from <see cref="Plant3DSceneData.LoadAllGlbMeshes"/>,
/// builds a cached normalized 3D triangle display list, and renders a perspective-projected,
/// directionally-lit 3D model turntable synchronized with equipment status and load.
/// </summary>
public sealed class Equipment3DPreviewControl : Control
{
    public static readonly StyledProperty<string> EquipmentIdProperty =
        AvaloniaProperty.Register<Equipment3DPreviewControl, string>(nameof(EquipmentId), "CR-101");

    public static readonly StyledProperty<EquipmentStatus> StatusProperty =
        AvaloniaProperty.Register<Equipment3DPreviewControl, EquipmentStatus>(nameof(Status), EquipmentStatus.Running);

    public static readonly StyledProperty<double> LoadPercentProperty =
        AvaloniaProperty.Register<Equipment3DPreviewControl, double>(nameof(LoadPercent), 75.0);

    private readonly record struct PreviewTriangle(
        Vector3 V0,
        Vector3 V1,
        Vector3 V2,
        Vector3 Normal,
        Vector3 BaseColor,
        float Metallic,
        float Roughness);

    private sealed class PreviewMesh
    {
        public required string GlbName { get; init; }
        public required PreviewTriangle[] Triangles { get; init; }
        public required int OriginalTriangleCount { get; init; }
    }

    private static readonly ConcurrentDictionary<string, PreviewMesh> PreviewCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#09090B"));
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.Parse("#27272A")), 1.0);
    private static readonly IPen GridFloorPen = new Pen(new SolidColorBrush(Color.Parse("#241E12")), 1.0);
    private static readonly IPen WireframeEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(48, 217, 119, 6)), 0.55);
    private static readonly IBrush CyanTextBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.Parse("#A1A1AA"));
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush FaultBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#71717A"));

    private readonly DispatcherTimer _timer;
    private float _yawRadians = 0.55f;

    public string EquipmentId
    {
        get => GetValue(EquipmentIdProperty);
        set => SetValue(EquipmentIdProperty, value);
    }

    public EquipmentStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public double LoadPercent
    {
        get => GetValue(LoadPercentProperty);
        set => SetValue(LoadPercentProperty, value);
    }

    public float YawRadians => _yawRadians;

    static Equipment3DPreviewControl()
    {
        AffectsRender<Equipment3DPreviewControl>(
            EquipmentIdProperty,
            StatusProperty,
            LoadPercentProperty);
    }

    public Equipment3DPreviewControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) }; // 25 fps turntable
        _timer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            AdvanceTurntable(0.04f);
        };
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public void AdvanceTurntable(float dtSeconds)
    {
        if (Status is EquipmentStatus.Stopped or EquipmentStatus.Offline or EquipmentStatus.Fault)
            return;

        float rate = 0.48f + (float)Math.Clamp(LoadPercent / 100.0, 0.1, 1.2) * 0.35f;
        _yawRadians = (_yawRadians + dtSeconds * rate) % (MathF.PI * 2f);
        InvalidateVisual();
    }

    public static string ResolveGlbForEquipment(string? equipmentId)
    {
        if (string.IsNullOrWhiteSpace(equipmentId))
            return "jaw_crusher_jc101.glb";

        string id = equipmentId.Trim().ToUpperInvariant();
        if (id.StartsWith("CV-", StringComparison.Ordinal))
            return "conveyor_segment.glb";

        return id switch
        {
            "CR-101" or "JC-101" => "jaw_crusher_jc101.glb",
            "DRC-101" => "double_roll_crusher_drc101.glb",
            "SC-102" or "SS-101" => "central_sizing_screen_ss101.glb",
            "DS-201" => "desliming_screen_ds201.glb",
            "BN-101" => "feeder_bin_bn101.glb",
            "VF-101" or "AF-001" => "vibro_feeder_vf101.glb",
            "BN-201" or "SL-601" => "surge_bin_bn201.glb",
            "SP-001" or "SP-301" or "SP-302" => "stockpile_cone.glb",
            "FEL-101" => "wheel_loader_chassis.glb",
            "TK-201" => "thickener_tk201.glb",
            "PP-201" or "PP-202" or "PP-203" or "PP-401" or "PP-402" or "AB-401" => "centrifugal_pump.glb",
            "TK-301" => "magnetite_tank_tk301.glb",
            "MS-301" or "CF-501" => "magnetic_separator_ms301.glb",
            "DM-301" or "WP-301" or "DMC-301" or "SP-401" or "FC-401" or "FP-501" or "AN-601" or "LD-601" => "wash_plant_module_wp301.glb",
            _ => "jaw_crusher_jc101.glb"
        };
    }

    private static PreviewMesh GetOrBuildPreviewMesh(string glbFileName)
    {
        return PreviewCache.GetOrAdd(glbFileName, static name =>
        {
            var meshes = Plant3DSceneData.LoadAllGlbMeshes();
            if (!meshes.TryGetValue(name, out var glb) || glb.TriangleCount == 0)
            {
                glb = meshes.Values.First();
            }

            Vector3 center = (glb.BoundsMin + glb.BoundsMax) * 0.5f;
            Vector3 size = glb.BoundsMax - glb.BoundsMin;
            float maxExtent = Math.Max(Math.Max(size.X, size.Y), Math.Max(size.Z, 0.01f));
            float invScale = 1.95f / maxExtent;

            int totalTris = glb.Indices.Length / 3;
            const int maxPreviewTris = 280;
            int stride = Math.Max(1, totalTris / maxPreviewTris);

            var list = new List<PreviewTriangle>(Math.Min(totalTris, maxPreviewTris + 16));
            var verts = glb.Vertices;
            var idx = glb.Indices;

            for (int t = 0; t < totalTris; t += stride)
            {
                int i0 = (int)idx[t * 3] * GlbMeshData.FloatsPerVertex;
                int i1 = (int)idx[t * 3 + 1] * GlbMeshData.FloatsPerVertex;
                int i2 = (int)idx[t * 3 + 2] * GlbMeshData.FloatsPerVertex;

                var p0 = (new Vector3(verts[i0], verts[i0 + 1], verts[i0 + 2]) - center) * invScale;
                var p1 = (new Vector3(verts[i1], verts[i1 + 1], verts[i1 + 2]) - center) * invScale;
                var p2 = (new Vector3(verts[i2], verts[i2 + 1], verts[i2 + 2]) - center) * invScale;

                var n0 = new Vector3(verts[i0 + 3], verts[i0 + 4], verts[i0 + 5]);
                var n1 = new Vector3(verts[i1 + 3], verts[i1 + 4], verts[i1 + 5]);
                var n2 = new Vector3(verts[i2 + 3], verts[i2 + 4], verts[i2 + 5]);
                var nAvg = n0 + n1 + n2;
                if (nAvg.LengthSquared() < 1e-4f)
                    nAvg = Vector3.Cross(p1 - p0, p2 - p0);
                var normal = nAvg.LengthSquared() > 1e-6f ? Vector3.Normalize(nAvg) : Vector3.UnitY;

                var c0 = new Vector3(verts[i0 + 6], verts[i0 + 7], verts[i0 + 8]);
                var c1 = new Vector3(verts[i1 + 6], verts[i1 + 7], verts[i1 + 8]);
                var c2 = new Vector3(verts[i2 + 6], verts[i2 + 7], verts[i2 + 8]);
                var baseCol = (c0 + c1 + c2) / 3f;

                float met = verts[i0 + 9];
                float rough = verts[i0 + 10];
                list.Add(new PreviewTriangle(p0, p1, p2, normal, baseCol, met, rough));
            }

            return new PreviewMesh
            {
                GlbName = name,
                Triangles = list.ToArray(),
                OriginalTriangleCount = glb.TriangleCount
            };
        });
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 24 || h <= 24) return;

        var rect = new Rect(0, 0, w, h);
        context.FillRectangle(BgBrush, rect, 6f);

        string glbName = ResolveGlbForEquipment(EquipmentId);
        var preview = GetOrBuildPreviewMesh(glbName);

        // Conveyor cards pitch the 3D segment according to their actual GA slope
        float pitchOffset = 0f;
        if (EquipmentId.StartsWith("CV-", StringComparison.OrdinalIgnoreCase))
        {
            var spec = Plant3DSceneData.Conveyors.FirstOrDefault(s =>
                string.Equals(s.Id, EquipmentId, StringComparison.OrdinalIgnoreCase));
            if (spec is not null)
            {
                Vector3 delta = spec.HeadPos - spec.TailPos;
                float horiz = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                if (horiz > 0.1f)
                    pitchOffset = Math.Clamp(MathF.Atan2(delta.Y, horiz) * 0.5f, -0.35f, 0.35f);
            }
        }

        var rot = Matrix4x4.CreateRotationZ(pitchOffset)
                * Matrix4x4.CreateRotationY(_yawRadians)
                * Matrix4x4.CreateRotationX(0.34f);

        double cx = w * 0.5;
        double cy = h * 0.56;
        double scale = Math.Min(w, h) * 0.46;
        const float camDist = 3.15f;

        // 1. Render 3D turntable reference grid pad below model
        DrawTurntableFloorGrid(context, rot, cx, cy, scale, camDist);

        // 2. Transform and depth-sort 3D triangles (Painter's algorithm)
        var tris = preview.Triangles;
        var sorted = new (float Depth, Point P0, Point P1, Point P2, Color Shade)[tris.Length];
        int validCount = 0;

        Vector3 keyLight = Vector3.Normalize(new Vector3(0.52f, 0.78f, -0.35f));
        Vector3 fillLight = Vector3.Normalize(new Vector3(-0.60f, 0.25f, -0.55f));
        Vector3 viewDir = new(0f, 0f, -1f);

        Vector3 statusTint = Status switch
        {
            EquipmentStatus.Warning => new Vector3(0.96f, 0.62f, 0.04f),
            EquipmentStatus.Fault => new Vector3(0.94f, 0.27f, 0.27f),
            EquipmentStatus.Stopped or EquipmentStatus.Offline => new Vector3(0.45f, 0.52f, 0.62f),
            _ => new Vector3(0.14f, 0.74f, 0.96f)
        };

        for (int i = 0; i < tris.Length; i++)
        {
            ref readonly var tri = ref tris[i];
            var v0 = Vector3.Transform(tri.V0, rot);
            var v1 = Vector3.Transform(tri.V1, rot);
            var v2 = Vector3.Transform(tri.V2, rot);
            var n = Vector3.TransformNormal(tri.Normal, rot);

            float z0 = camDist + v0.Z;
            float z1 = camDist + v1.Z;
            float z2 = camDist + v2.Z;
            if (z0 <= 0.2f || z1 <= 0.2f || z2 <= 0.2f) continue;

            var s0 = new Point(cx + (v0.X / z0) * scale * 2.2, cy - (v0.Y / z0) * scale * 2.2);
            var s1 = new Point(cx + (v1.X / z1) * scale * 2.2, cy - (v1.Y / z1) * scale * 2.2);
            var s2 = new Point(cx + (v2.X / z2) * scale * 2.2, cy - (v2.Y / z2) * scale * 2.2);

            if (n.Z > 0f) n = -n; // Two-sided industrial mesh shading
            float ndotlKey = Math.Max(0f, Vector3.Dot(n, keyLight));
            float ndotlFill = Math.Max(0f, Vector3.Dot(n, fillLight));
            var halfVec = Vector3.Normalize(keyLight + viewDir);
            float spec = MathF.Pow(Math.Max(0f, Vector3.Dot(n, halfVec)), 16f) * (0.25f + tri.Metallic * 0.55f);
            float rim = MathF.Pow(1f - Math.Clamp(MathF.Abs(n.Z), 0f, 1f), 2.2f) * 0.28f;

            Vector3 lit = tri.BaseColor * (0.26f + ndotlKey * 0.78f + ndotlFill * 0.28f)
                        + new Vector3(spec)
                        + statusTint * rim;

            byte r = (byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(lit.X, 0f, 1f), 1f / 2.1f) * 255f), 18, 255);
            byte g = (byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(lit.Y, 0f, 1f), 1f / 2.1f) * 255f), 22, 255);
            byte b = (byte)Math.Clamp((int)(MathF.Pow(Math.Clamp(lit.Z, 0f, 1f), 1f / 2.1f) * 255f), 28, 255);

            sorted[validCount++] = ((z0 + z1 + z2) / 3f, s0, s1, s2, Color.FromRgb(r, g, b));
        }

        Array.Sort(sorted, 0, validCount, Comparer<(float Depth, Point P0, Point P1, Point P2, Color Shade)>.Create(
            static (a, b) => b.Depth.CompareTo(a.Depth)));

        for (int i = 0; i < validCount; i++)
        {
            var item = sorted[i];
            var geom = new StreamGeometry();
            using (var g = geom.Open())
            {
                g.BeginFigure(item.P0, isFilled: true);
                g.LineTo(item.P1);
                g.LineTo(item.P2);
                g.EndFigure(isClosed: true);
            }
            context.DrawGeometry(new SolidColorBrush(item.Shade), WireframeEdgePen, geom);
        }

        // 3. HUD overlays: 3D GLB model filename, triangle count & turntable yaw
        var ftTitle = new FormattedText(
            $"3D PREVIEW · {glbName}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            9.5,
            CyanTextBrush);
        context.DrawText(ftTitle, new Point(8, 5));

        IBrush stBrush = Status switch
        {
            EquipmentStatus.Running => RunningBrush,
            EquipmentStatus.Warning => WarningBrush,
            EquipmentStatus.Fault => FaultBrush,
            _ => StoppedBrush
        };
        int yawDeg = (int)Math.Round(_yawRadians * 180.0 / Math.PI) % 360;
        var ftMeta = new FormattedText(
            $"{preview.OriginalTriangleCount} tris · {yawDeg}°",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            9.0,
            stBrush);
        context.DrawText(ftMeta, new Point(Math.Max(8, w - ftMeta.Width - 8), h - 16));

        context.DrawRectangle(null, FramePen, rect, 6f, 6f);
    }

    private static void DrawTurntableFloorGrid(
        DrawingContext ctx,
        Matrix4x4 rot,
        double cx,
        double cy,
        double scale,
        float camDist)
    {
        const float yFloor = -0.82f;
        const float half = 1.05f;
        for (int i = -2; i <= 2; i++)
        {
            float t = i * (half / 2f);
            DrawLine3D(ctx, rot, new Vector3(-half, yFloor, t), new Vector3(half, yFloor, t), cx, cy, scale, camDist, GridFloorPen);
            DrawLine3D(ctx, rot, new Vector3(t, yFloor, -half), new Vector3(t, yFloor, half), cx, cy, scale, camDist, GridFloorPen);
        }
    }

    private static void DrawLine3D(
        DrawingContext ctx,
        Matrix4x4 rot,
        Vector3 a,
        Vector3 b,
        double cx,
        double cy,
        double scale,
        float camDist,
        IPen pen)
    {
        var va = Vector3.Transform(a, rot);
        var vb = Vector3.Transform(b, rot);
        float za = camDist + va.Z;
        float zb = camDist + vb.Z;
        if (za <= 0.2f || zb <= 0.2f) return;
        var pa = new Point(cx + (va.X / za) * scale * 2.2, cy - (va.Y / za) * scale * 2.2);
        var pb = new Point(cx + (vb.X / zb) * scale * 2.2, cy - (vb.Y / zb) * scale * 2.2);
        ctx.DrawLine(pen, pa, pb);
    }
}
