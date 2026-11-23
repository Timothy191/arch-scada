using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls.ThreeD;

/// <summary>
/// Hardware-accelerated OpenGL 3.3 Core / OpenGL ES 3.0 viewport for the CHPP 3D Digital Twin.
/// Renders Blender-exported .glb meshes with true hardware depth buffering, directional +
/// hemisphere Blinn-Phong lighting, orthogonal 3D piping, foundation pads, live tank fill levels,
/// telemetry-coupled particles, alarm halos, and selection/hover highlights.
/// </summary>
public sealed unsafe class Plant3DOpenGlCanvas : OpenGlControlBase
{
    private const int GL_COLOR_BUFFER_BIT = 0x00004000;
    private const int GL_DEPTH_BUFFER_BIT = 0x00000100;
    private const int GL_DEPTH_TEST = 0x0B71;
    private const int GL_CULL_FACE = 0x0B44;
    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const int GL_TRIANGLES = 0x0004;
    private const int GL_LINES = 0x0001;
    private const int GL_UNSIGNED_INT = 0x1405;
    private const int GL_FLOAT = 0x1406;
    private const int GL_ARRAY_BUFFER = 0x8892;
    private const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    private const int GL_STATIC_DRAW = 0x88E4;
    private const int GL_VERTEX_SHADER = 0x8B31;
    private const int GL_FRAGMENT_SHADER = 0x8B30;
    private const int GL_COMPILE_STATUS = 0x8B81;
    private const int GL_LINK_STATUS = 0x8B82;

    // Minimal portable GL 3.3 / GLES 3.0 function delegates bound via GlInterface.GetProcAddress
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlViewportDel(int x, int y, int w, int h);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlClearColorDel(float r, float g, float b, float a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlClearDel(int mask);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlEnableDel(int cap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDisableDel(int cap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBlendFuncDel(int sfactor, int dfactor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDepthMaskDel(byte flag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlCreateShaderDel(int type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlShaderSourceDel(int shader, int count, byte** strings, int* length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlCompileShaderDel(int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetShaderivDel(int shader, int pname, int* param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteShaderDel(int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlCreateProgramDel();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlAttachShaderDel(int program, int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlLinkProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetProgramivDel(int program, int pname, int* param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUseProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlGetUniformLocationDel(int program, byte* name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniformMatrix4fvDel(int loc, int count, byte transpose, float* value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform3fDel(int loc, float x, float y, float z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform4fDel(int loc, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform1fDel(int loc, float v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenVertexArraysDel(int n, uint* arrays);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindVertexArrayDel(uint array);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteVertexArraysDel(int n, uint* arrays);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenBuffersDel(int n, uint* buffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindBufferDel(int target, uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBufferDataDel(int target, nint size, void* data, int usage);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteBuffersDel(int n, uint* buffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlEnableVertexAttribArrayDel(uint index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlVertexAttribPointerDel(uint index, int size, int type, byte normalized, int stride, nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawElementsDel(int mode, int count, int type, nint indices);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawArraysDel(int mode, int first, int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindFramebufferDel(int target, int framebuffer);

    private GlViewportDel? _glViewport;
    private GlClearColorDel? _glClearColor;
    private GlClearDel? _glClear;
    private GlEnableDel? _glEnable;
    private GlDisableDel? _glDisable;
    private GlBlendFuncDel? _glBlendFunc;
    private GlDepthMaskDel? _glDepthMask;
    private GlCreateShaderDel? _glCreateShader;
    private GlShaderSourceDel? _glShaderSource;
    private GlCompileShaderDel? _glCompileShader;
    private GlGetShaderivDel? _glGetShaderiv;
    private GlDeleteShaderDel? _glDeleteShader;
    private GlCreateProgramDel? _glCreateProgram;
    private GlAttachShaderDel? _glAttachShader;
    private GlLinkProgramDel? _glLinkProgram;
    private GlGetProgramivDel? _glGetProgramiv;
    private GlUseProgramDel? _glUseProgram;
    private GlDeleteProgramDel? _glDeleteProgram;
    private GlGetUniformLocationDel? _glGetUniformLocation;
    private GlUniformMatrix4fvDel? _glUniformMatrix4fv;
    private GlUniform3fDel? _glUniform3f;
    private GlUniform4fDel? _glUniform4f;
    private GlUniform1fDel? _glUniform1f;
    private GlGenVertexArraysDel? _glGenVertexArrays;
    private GlBindVertexArrayDel? _glBindVertexArray;
    private GlDeleteVertexArraysDel? _glDeleteVertexArrays;
    private GlGenBuffersDel? _glGenBuffers;
    private GlBindBufferDel? _glBindBuffer;
    private GlBufferDataDel? _glBufferData;
    private GlDeleteBuffersDel? _glDeleteBuffers;
    private GlEnableVertexAttribArrayDel? _glEnableVertexAttribArray;
    private GlVertexAttribPointerDel? _glVertexAttribPointer;
    private GlDrawElementsDel? _glDrawElements;
    private GlDrawArraysDel? _glDrawArrays;

    private sealed class GpuMesh
    {
        public uint Vao;
        public uint Vbo;
        public uint Ebo;
        public int IndexCount;
        public bool IsLines;
    }

    private readonly Dictionary<string, GpuMesh> _glbGpuMeshes = new(StringComparer.OrdinalIgnoreCase);
    private GpuMesh? _unitBox;
    private GpuMesh? _unitCylinder;
    private GpuMesh? _unitRing;
    private GpuMesh? _gridLines;
    private GpuMesh? _pipesMesh;

    private int _program;
    private int _uModel = -1;
    private int _uViewProj = -1;
    private int _uCamPos = -1;
    private int _uTint = -1;
    private int _uTintMix = -1;
    private int _uAlpha = -1;
    private int _uUnlit = -1;

    // Shared render state pushed from parent Plant3DViewportControl
    public double CameraYaw { get; set; } = -0.62;
    public double CameraPitch { get; set; } = 0.52;
    public double CameraZoom { get; set; } = 1.05;
    public double TargetX { get; set; }
    public double TargetZ { get; set; } = 0.5;
    public double SimTime { get; set; }
    public IReadOnlyList<EquipmentItem>? Equipment { get; set; }
    public Unity3DEffectSettings? Effects { get; set; }
    public string? SelectedEquipmentId { get; set; }
    public string? HoveredEquipmentId { get; set; }

    public bool IsHardwareActive { get; private set; }
    public int LoadedGlbCount => _glbGpuMeshes.Count;

    private T? LoadProc<T>(GlInterface gl, string name) where T : Delegate
    {
        var ptr = gl.GetProcAddress(name);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        IsHardwareActive = false;

        try
        {
            _glViewport = LoadProc<GlViewportDel>(gl, "glViewport");
            _glClearColor = LoadProc<GlClearColorDel>(gl, "glClearColor");
            _glClear = LoadProc<GlClearDel>(gl, "glClear");
            _glEnable = LoadProc<GlEnableDel>(gl, "glEnable");
            _glDisable = LoadProc<GlDisableDel>(gl, "glDisable");
            _glBlendFunc = LoadProc<GlBlendFuncDel>(gl, "glBlendFunc");
            _glDepthMask = LoadProc<GlDepthMaskDel>(gl, "glDepthMask");
            _glCreateShader = LoadProc<GlCreateShaderDel>(gl, "glCreateShader");
            _glShaderSource = LoadProc<GlShaderSourceDel>(gl, "glShaderSource");
            _glCompileShader = LoadProc<GlCompileShaderDel>(gl, "glCompileShader");
            _glGetShaderiv = LoadProc<GlGetShaderivDel>(gl, "glGetShaderiv");
            _glDeleteShader = LoadProc<GlDeleteShaderDel>(gl, "glDeleteShader");
            _glCreateProgram = LoadProc<GlCreateProgramDel>(gl, "glCreateProgram");
            _glAttachShader = LoadProc<GlAttachShaderDel>(gl, "glAttachShader");
            _glLinkProgram = LoadProc<GlLinkProgramDel>(gl, "glLinkProgram");
            _glGetProgramiv = LoadProc<GlGetProgramivDel>(gl, "glGetProgramiv");
            _glUseProgram = LoadProc<GlUseProgramDel>(gl, "glUseProgram");
            _glDeleteProgram = LoadProc<GlDeleteProgramDel>(gl, "glDeleteProgram");
            _glGetUniformLocation = LoadProc<GlGetUniformLocationDel>(gl, "glGetUniformLocation");
            _glUniformMatrix4fv = LoadProc<GlUniformMatrix4fvDel>(gl, "glUniformMatrix4fv");
            _glUniform3f = LoadProc<GlUniform3fDel>(gl, "glUniform3f");
            _glUniform4f = LoadProc<GlUniform4fDel>(gl, "glUniform4f");
            _glUniform1f = LoadProc<GlUniform1fDel>(gl, "glUniform1f");
            _glGenVertexArrays = LoadProc<GlGenVertexArraysDel>(gl, "glGenVertexArrays");
            _glBindVertexArray = LoadProc<GlBindVertexArrayDel>(gl, "glBindVertexArray");
            _glDeleteVertexArrays = LoadProc<GlDeleteVertexArraysDel>(gl, "glDeleteVertexArrays");
            _glGenBuffers = LoadProc<GlGenBuffersDel>(gl, "glGenBuffers");
            _glBindBuffer = LoadProc<GlBindBufferDel>(gl, "glBindBuffer");
            _glBufferData = LoadProc<GlBufferDataDel>(gl, "glBufferData");
            _glDeleteBuffers = LoadProc<GlDeleteBuffersDel>(gl, "glDeleteBuffers");
            _glEnableVertexAttribArray = LoadProc<GlEnableVertexAttribArrayDel>(gl, "glEnableVertexAttribArray");
            _glVertexAttribPointer = LoadProc<GlVertexAttribPointerDel>(gl, "glVertexAttribPointer");
            _glDrawElements = LoadProc<GlDrawElementsDel>(gl, "glDrawElements");
            _glDrawArrays = LoadProc<GlDrawArraysDel>(gl, "glDrawArrays");

            if (_glCreateProgram is null || _glGenVertexArrays is null || _glDrawElements is null)
                return;

            bool isEs = GlVersion.Type == GlProfileType.OpenGLES;
            _program = BuildShaderProgram(isEs);
            if (_program == 0)
                return;

            _uModel = GetUniformLoc("uModel");
            _uViewProj = GetUniformLoc("uViewProj");
            _uCamPos = GetUniformLoc("uCamPos");
            _uTint = GetUniformLoc("uTint");
            _uTintMix = GetUniformLoc("uTintMix");
            _uAlpha = GetUniformLoc("uAlpha");
            _uUnlit = GetUniformLoc("uUnlit");

            // Upload all Blender-exported GLB meshes
            foreach (var (fileName, meshData) in Plant3DSceneData.LoadAllGlbMeshes())
            {
                _glbGpuMeshes[fileName] = UploadIndexedMesh(meshData.Vertices, meshData.Indices);
            }

            // Upload shared procedural GPU meshes
            _unitBox = BuildUnitBoxMesh();
            _unitCylinder = BuildUnitCylinderMesh(20);
            _unitRing = BuildUnitRingMesh(32, 0.88f, 1.0f);
            _gridLines = BuildGroundGridLines();
            _pipesMesh = BuildOrthogonalPipesMesh();

            IsHardwareActive = true;
        }
        catch
        {
            IsHardwareActive = false;
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        IsHardwareActive = false;
        foreach (var m in _glbGpuMeshes.Values) DeleteMesh(m);
        _glbGpuMeshes.Clear();
        DeleteMesh(_unitBox); _unitBox = null;
        DeleteMesh(_unitCylinder); _unitCylinder = null;
        DeleteMesh(_unitRing); _unitRing = null;
        DeleteMesh(_gridLines); _gridLines = null;
        DeleteMesh(_pipesMesh); _pipesMesh = null;

        if (_program != 0)
        {
            _glDeleteProgram?.Invoke(_program);
            _program = 0;
        }
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!IsHardwareActive || _program == 0)
            return;

        double scale = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int pw = Math.Max(1, (int)(Bounds.Width * scale));
        int ph = Math.Max(1, (int)(Bounds.Height * scale));

        _glViewport!(0, 0, pw, ph);
        _glClearColor!(0.031f, 0.055f, 0.106f, 1.0f);
        _glClear!(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        _glEnable!(GL_DEPTH_TEST);
        _glDisable!(GL_CULL_FACE);
        _glEnable!(GL_BLEND);
        _glBlendFunc!(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        _glUseProgram!(_program);

        var fx = Effects ?? new Unity3DEffectSettings();
        var (view, proj, camPos) = Plant3DSceneData.BuildCameraMatrices(
            CameraYaw, CameraPitch, CameraZoom, TargetX, TargetZ,
            Math.Max(1.0, Bounds.Width), Math.Max(1.0, Bounds.Height),
            fx.IsOrthographic);
        var viewProj = view * proj;

        SetMatrix(_uViewProj, viewProj);
        _glUniform3f!(_uCamPos, camPos.X, camPos.Y, camPos.Z);

        var eqMap = new Dictionary<string, EquipmentItem>(StringComparer.OrdinalIgnoreCase);
        if (Equipment is not null)
        {
            foreach (var e in Equipment) eqMap[e.Id] = e;
        }

        // 1. Ground Grid
        if (_gridLines is not null)
        {
            SetMatrix(_uModel, Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.22f, 0.65f, 0.92f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.22f);
            _glUniform1f!(_uUnlit, 1.0f);
            DrawMesh(_gridLines);
        }

        // 2. Concrete Foundation Pads + Circuit Accent Trim
        if (_unitBox is not null)
        {
            _glUniform1f!(_uUnlit, 0.0f);
            _glUniform1f!(_uAlpha, 1.0f);
            foreach (var pad in Plant3DSceneData.FoundationPads)
            {
                var padMat = Matrix4x4.CreateScale(pad.Size) * Matrix4x4.CreateTranslation(pad.Center);
                SetMatrix(_uModel, padMat);
                _glUniform3f!(_uTint, 0.14f, 0.18f, 0.25f);
                _glUniform1f!(_uTintMix, 1.0f);
                DrawMesh(_unitBox);

                // Subtle colored zone trim strip along front edge
                var trimMat = Matrix4x4.CreateScale(pad.Size.X, 0.04f, 0.12f)
                            * Matrix4x4.CreateTranslation(pad.Center.X, 0.02f, pad.Center.Z + pad.Size.Z * 0.5f - 0.06f);
                SetMatrix(_uModel, trimMat);
                _glUniform3f!(_uTint, pad.AccentColor.R / 255f, pad.AccentColor.G / 255f, pad.AccentColor.B / 255f);
                DrawMesh(_unitBox);
            }
        }

        // 3. Ground Contact Shadows
        if (fx.ShowGroundShadows && _unitCylinder is not null)
        {
            _glDepthMask!(0);
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform3f!(_uTint, 0.01f, 0.02f, 0.05f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.52f);
            foreach (var p in Plant3DSceneData.Placements)
            {
                float rx = p.HalfExtents.X * 1.05f;
                float rz = p.HalfExtents.Z * 1.05f;
                var sm = Matrix4x4.CreateScale(rx, 0.02f, rz)
                       * Matrix4x4.CreateTranslation(p.Position.X + 0.18f, 0.015f, p.Position.Z + 0.18f);
                SetMatrix(_uModel, sm);
                DrawMesh(_unitCylinder);
            }
            _glDepthMask!(1);
        }

        // 4. Orthogonal 3D Process Piping
        if (_pipesMesh is not null)
        {
            SetMatrix(_uModel, Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.55f, 0.64f, 0.75f);
            _glUniform1f!(_uTintMix, 0.35f);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);
            DrawMesh(_pipesMesh);
        }

        // 5. All 14 Plant Equipment Assemblies (Blender .glb meshes)
        foreach (var p in Plant3DSceneData.Placements)
        {
            eqMap.TryGetValue(p.Id, out var eq);

            float vibY = 0f;
            if (p.Vibrates && fx.EnableVibrationPhysics && eq is not null && eq.Status != EquipmentStatus.Stopped)
            {
                float amp = 0.018f + 0.025f * (float)(eq.LoadPercent / 100.0);
                vibY = MathF.Sin((float)(SimTime * 28.0)) * amp;
            }

            var model = Matrix4x4.CreateScale(p.Scale)
                      * Matrix4x4.CreateRotationY(p.YawDegrees * (MathF.PI / 180f))
                      * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + vibY, p.Position.Z);
            SetMatrix(_uModel, model);

            var tint = Plant3DSceneData.GetStatusTint(p, eq, fx);
            bool isSelected = string.Equals(SelectedEquipmentId, p.Id, StringComparison.OrdinalIgnoreCase);
            bool isHovered = string.Equals(HoveredEquipmentId, p.Id, StringComparison.OrdinalIgnoreCase);

            float tintMix = fx.ShowThermalHeatmap ? 0.78f
                : (eq?.Status is EquipmentStatus.Fault or EquipmentStatus.Warning or EquipmentStatus.Stopped ? 0.58f : 0.08f);
            if (isSelected) { tint = Color.Parse("#38BDF8"); tintMix = MathF.Max(tintMix, 0.28f); }
            else if (isHovered) { tint = Color.Parse("#7DD3FC"); tintMix = MathF.Max(tintMix, 0.20f); }

            _glUniform3f!(_uTint, tint.R / 255f, tint.G / 255f, tint.B / 255f);
            _glUniform1f!(_uTintMix, tintMix);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);

            if (_glbGpuMeshes.TryGetValue(p.GlbFileName, out var gpuMesh))
            {
                DrawMesh(gpuMesh);
            }
            else if (_unitBox is not null)
            {
                var fbMat = Matrix4x4.CreateScale(p.HalfExtents.X * 2f, p.HalfExtents.Y * 2f, p.HalfExtents.Z * 2f)
                          * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.HalfExtents.Y + vibY, p.Position.Z);
                SetMatrix(_uModel, fbMat);
                DrawMesh(_unitBox);
            }

            // Rotating animated indicator (TK-201 rake sweep / MS-301 wet drum indicator)
            if (p.HasRotatingTop && _unitBox is not null)
            {
                bool running = eq is null || eq.Status != EquipmentStatus.Stopped;
                float ang = (fx.AnimateConveyors && running) ? (float)(SimTime * (p.Id == "TK-201" ? 0.45 : 1.6)) : 0.2f;
                var rotMat = Matrix4x4.CreateScale(p.RotatingTopLength, 0.14f, 0.24f)
                           * Matrix4x4.CreateRotationY(ang)
                           * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.RotatingTopY, p.Position.Z);
                SetMatrix(_uModel, rotMat);
                _glUniform3f!(_uTint, 0.22f, 0.78f, 0.98f);
                _glUniform1f!(_uTintMix, 1.0f);
                DrawMesh(_unitBox);
            }
        }

        // 6. Live 3D Fill-Level Indicators (translucent fluid/coal columns on TK-201, TK-301, FC-401, SL-601)
        if (_unitCylinder is not null)
        {
            _glDepthMask!(0);
            _glUniform1f!(_uUnlit, 0.2f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.42f);

            foreach (var p in Plant3DSceneData.Placements)
            {
                if (!p.HasFillLevel) continue;
                eqMap.TryGetValue(p.Id, out var eq);
                float fillFrac = eq is not null ? Math.Clamp((float)(eq.LoadPercent / 100.0), 0.12f, 0.96f) : 0.62f;
                float fillH = p.FillMaxHeight * fillFrac;
                var fillColor = p.Id == "SL-601" ? Color.Parse("#F59E0B")
                              : p.Id == "TK-301" ? Color.Parse("#14B8A6")
                              : Color.Parse("#38BDF8");

                var fm = Matrix4x4.CreateScale(p.FillRadius * 1.04f, fillH, p.FillRadius * 1.04f)
                       * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.FillBaseY + fillH * 0.5f, p.Position.Z);
                SetMatrix(_uModel, fm);
                _glUniform3f!(_uTint, fillColor.R / 255f, fillColor.G / 255f, fillColor.B / 255f);
                DrawMesh(_unitCylinder);
            }
            _glDepthMask!(1);
        }

        // 7. Telemetry-Coupled 3D Flow Particles (depth-tested!)
        if (fx.ShowFlowParticles && _unitBox is not null)
        {
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.95f);

            foreach (var route in Plant3DSceneData.PipeRoutes)
            {
                eqMap.TryGetValue(route.SourceEquipmentId, out var srcEq);
                double speed = Plant3DSceneData.GetFlowSpeedFactor(srcEq);
                if (speed <= 0.0) continue; // Paused when source equipment is Stopped/Fault

                var c = Plant3DSceneData.GetMediumColor(route.Medium);
                _glUniform3f!(_uTint, c.R / 255f, c.G / 255f, c.B / 255f);

                int count = 6;
                for (int i = 0; i < count; i++)
                {
                    float t = (float)((SimTime * 0.28 * speed + (double)i / count) % 1.0);
                    var pos = Plant3DSceneData.SamplePolyline(route.Waypoints, t);
                    var pm = Matrix4x4.CreateScale(0.18f) * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.14f, pos.Z);
                    SetMatrix(_uModel, pm);
                    DrawMesh(_unitBox);
                }
            }
        }

        // 8. Alarm Halos & Selection / Hover Rings
        if (_unitRing is not null)
        {
            _glDepthMask!(0);
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform1f!(_uTintMix, 1.0f);

            float pulse = 0.55f + 0.45f * MathF.Sin((float)(SimTime * 4.5));
            foreach (var p in Plant3DSceneData.Placements)
            {
                eqMap.TryGetValue(p.Id, out var eq);
                float baseR = MathF.Max(p.HalfExtents.X, p.HalfExtents.Z) + 0.35f;

                if (fx.ShowAlarmHalos && eq is not null && eq.Status is EquipmentStatus.Warning or EquipmentStatus.Fault)
                {
                    var ac = eq.Status == EquipmentStatus.Fault ? Color.Parse("#EF4444") : Color.Parse("#F59E0B");
                    var rm = Matrix4x4.CreateScale(baseR + 0.25f * pulse, 1f, baseR + 0.25f * pulse)
                           * Matrix4x4.CreateTranslation(p.Position.X, 0.08f, p.Position.Z);
                    SetMatrix(_uModel, rm);
                    _glUniform3f!(_uTint, ac.R / 255f, ac.G / 255f, ac.B / 255f);
                    _glUniform1f!(_uAlpha, 0.45f + 0.45f * pulse);
                    DrawMesh(_unitRing);
                }

                bool isSel = string.Equals(SelectedEquipmentId, p.Id, StringComparison.OrdinalIgnoreCase);
                bool isHov = string.Equals(HoveredEquipmentId, p.Id, StringComparison.OrdinalIgnoreCase);
                if (isSel || isHov)
                {
                    var sc = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
                    var rm = Matrix4x4.CreateScale(baseR + 0.15f, 1f, baseR + 0.15f)
                           * Matrix4x4.CreateTranslation(p.Position.X, 0.10f, p.Position.Z);
                    SetMatrix(_uModel, rm);
                    _glUniform3f!(_uTint, sc.R / 255f, sc.G / 255f, sc.B / 255f);
                    _glUniform1f!(_uAlpha, isSel ? 0.92f : 0.58f);
                    DrawMesh(_unitRing);
                }
            }
            _glDepthMask!(1);
        }

        _glBindVertexArray!(0);
        _glUseProgram!(0);
    }

    private int BuildShaderProgram(bool isEs)
    {
        string header = isEs ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";

        string vsSrc = header + @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec3 aColor;
layout(location = 3) in vec2 aPbr;

uniform mat4 uModel;
uniform mat4 uViewProj;

out vec3 vWorldPos;
out vec3 vNormal;
out vec3 vColor;
out vec2 vPbr;

void main() {
    vec4 wp = uModel * vec4(aPos, 1.0);
    vWorldPos = wp.xyz;
    vNormal = normalize(mat3(uModel) * aNormal);
    vColor = aColor;
    vPbr = aPbr;
    gl_Position = uViewProj * wp;
}
";

        string fsSrc = header + @"
in vec3 vWorldPos;
in vec3 vNormal;
in vec3 vColor;
in vec2 vPbr;

uniform vec3 uCamPos;
uniform vec3 uTint;
uniform float uTintMix;
uniform float uAlpha;
uniform float uUnlit;

out vec4 FragColor;

void main() {
    vec3 baseCol = mix(vColor, uTint, uTintMix);
    if (uUnlit > 0.5) {
        FragColor = vec4(baseCol, uAlpha);
        return;
    }

    vec3 N = normalize(vNormal);
    vec3 L = normalize(vec3(0.48, 0.82, 0.34));
    vec3 V = normalize(uCamPos - vWorldPos);
    vec3 H = normalize(L + V);

    float ndotl = max(dot(N, L), 0.0);
    float hemi = 0.5 + 0.5 * N.y;
    vec3 ambient = mix(vec3(0.14, 0.18, 0.25), vec3(0.32, 0.40, 0.52), hemi);

    float roughness = clamp(vPbr.y, 0.12, 1.0);
    float shininess = mix(96.0, 12.0, roughness);
    float spec = pow(max(dot(N, H), 0.0), shininess) * mix(0.20, 0.65, vPbr.x);

    // Subtle rim light for industrial silhouette separation
    float rim = pow(1.0 - max(dot(N, V), 0.0), 3.0) * 0.18;

    vec3 lit = baseCol * (ambient + vec3(0.88, 0.90, 0.94) * ndotl) + vec3(spec) + vec3(0.22, 0.52, 0.85) * rim;

    float dist = length(uCamPos - vWorldPos);
    float fog = clamp((dist - 22.0) / 38.0, 0.0, 0.42);
    vec3 finalCol = mix(lit, vec3(0.035, 0.062, 0.118), fog);

    FragColor = vec4(finalCol, uAlpha);
}
";

        int vs = CompileShader(GL_VERTEX_SHADER, vsSrc);
        int fs = CompileShader(GL_FRAGMENT_SHADER, fsSrc);
        if (vs == 0 || fs == 0)
        {
            if (vs != 0) _glDeleteShader!(vs);
            if (fs != 0) _glDeleteShader!(fs);
            return 0;
        }

        int prog = _glCreateProgram!();
        _glAttachShader!(prog, vs);
        _glAttachShader!(prog, fs);
        _glLinkProgram!(prog);

        int ok = 0;
        _glGetProgramiv!(prog, GL_LINK_STATUS, &ok);
        _glDeleteShader!(vs);
        _glDeleteShader!(fs);
        if (ok == 0)
        {
            _glDeleteProgram!(prog);
            return 0;
        }
        return prog;
    }

    private int CompileShader(int type, string src)
    {
        int shader = _glCreateShader!(type);
        var bytes = Encoding.UTF8.GetBytes(src);
        fixed (byte* p = bytes)
        {
            byte* pp = p;
            int len = bytes.Length;
            _glShaderSource!(shader, 1, &pp, &len);
        }
        _glCompileShader!(shader);
        int ok = 0;
        _glGetShaderiv!(shader, GL_COMPILE_STATUS, &ok);
        if (ok == 0)
        {
            _glDeleteShader!(shader);
            return 0;
        }
        return shader;
    }

    private int GetUniformLoc(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return _glGetUniformLocation!(_program, p);
    }

    private void SetMatrix(int loc, Matrix4x4 m)
    {
        if (loc < 0) return;
        float* ptr = stackalloc float[16];
        ptr[0] = m.M11; ptr[1] = m.M12; ptr[2] = m.M13; ptr[3] = m.M14;
        ptr[4] = m.M21; ptr[5] = m.M22; ptr[6] = m.M23; ptr[7] = m.M24;
        ptr[8] = m.M31; ptr[9] = m.M32; ptr[10] = m.M33; ptr[11] = m.M34;
        ptr[12] = m.M41; ptr[13] = m.M42; ptr[14] = m.M43; ptr[15] = m.M44;
        _glUniformMatrix4fv!(loc, 1, 0, ptr);
    }

    private GpuMesh UploadIndexedMesh(float[] vertices, uint[] indices)
    {
        uint vao = 0, vbo = 0, ebo = 0;
        _glGenVertexArrays!(1, &vao);
        _glGenBuffers!(1, &vbo);
        _glGenBuffers!(1, &ebo);

        _glBindVertexArray!(vao);

        _glBindBuffer!(GL_ARRAY_BUFFER, vbo);
        fixed (float* vp = vertices)
            _glBufferData!(GL_ARRAY_BUFFER, (nint)(vertices.Length * sizeof(float)), vp, GL_STATIC_DRAW);

        _glBindBuffer!(GL_ELEMENT_ARRAY_BUFFER, ebo);
        fixed (uint* ip = indices)
            _glBufferData!(GL_ELEMENT_ARRAY_BUFFER, (nint)(indices.Length * sizeof(uint)), ip, GL_STATIC_DRAW);

        int stride = GlbMeshData.FloatsPerVertex * sizeof(float);
        _glEnableVertexAttribArray!(0);
        _glVertexAttribPointer!(0, 3, GL_FLOAT, 0, stride, 0);
        _glEnableVertexAttribArray!(1);
        _glVertexAttribPointer!(1, 3, GL_FLOAT, 0, stride, 3 * sizeof(float));
        _glEnableVertexAttribArray!(2);
        _glVertexAttribPointer!(2, 3, GL_FLOAT, 0, stride, 6 * sizeof(float));
        _glEnableVertexAttribArray!(3);
        _glVertexAttribPointer!(3, 2, GL_FLOAT, 0, stride, 9 * sizeof(float));

        _glBindVertexArray!(0);
        return new GpuMesh { Vao = vao, Vbo = vbo, Ebo = ebo, IndexCount = indices.Length };
    }

    private GpuMesh UploadLineMesh(float[] vertices)
    {
        uint vao = 0, vbo = 0;
        _glGenVertexArrays!(1, &vao);
        _glGenBuffers!(1, &vbo);
        _glBindVertexArray!(vao);
        _glBindBuffer!(GL_ARRAY_BUFFER, vbo);
        fixed (float* vp = vertices)
            _glBufferData!(GL_ARRAY_BUFFER, (nint)(vertices.Length * sizeof(float)), vp, GL_STATIC_DRAW);

        int stride = GlbMeshData.FloatsPerVertex * sizeof(float);
        _glEnableVertexAttribArray!(0);
        _glVertexAttribPointer!(0, 3, GL_FLOAT, 0, stride, 0);
        _glEnableVertexAttribArray!(1);
        _glVertexAttribPointer!(1, 3, GL_FLOAT, 0, stride, 3 * sizeof(float));
        _glEnableVertexAttribArray!(2);
        _glVertexAttribPointer!(2, 3, GL_FLOAT, 0, stride, 6 * sizeof(float));
        _glEnableVertexAttribArray!(3);
        _glVertexAttribPointer!(3, 2, GL_FLOAT, 0, stride, 9 * sizeof(float));
        _glBindVertexArray!(0);
        return new GpuMesh { Vao = vao, Vbo = vbo, IndexCount = vertices.Length / GlbMeshData.FloatsPerVertex, IsLines = true };
    }

    private void DrawMesh(GpuMesh m)
    {
        _glBindVertexArray!(m.Vao);
        if (m.IsLines)
            _glDrawArrays!(GL_LINES, 0, m.IndexCount);
        else
            _glDrawElements!(GL_TRIANGLES, m.IndexCount, GL_UNSIGNED_INT, 0);
    }

    private void DeleteMesh(GpuMesh? m)
    {
        if (m is null) return;
        uint vao = m.Vao, vbo = m.Vbo, ebo = m.Ebo;
        if (vao != 0) _glDeleteVertexArrays?.Invoke(1, &vao);
        if (vbo != 0) _glDeleteBuffers?.Invoke(1, &vbo);
        if (ebo != 0) _glDeleteBuffers?.Invoke(1, &ebo);
    }

    private static void AddVert(List<float> v, Vector3 p, Vector3 n, float r = 1f, float g = 1f, float b = 1f, float met = 0.4f, float rough = 0.5f)
    {
        v.Add(p.X); v.Add(p.Y); v.Add(p.Z);
        v.Add(n.X); v.Add(n.Y); v.Add(n.Z);
        v.Add(r); v.Add(g); v.Add(b);
        v.Add(met); v.Add(rough);
    }

    private GpuMesh BuildUnitBoxMesh()
    {
        var v = new List<float>();
        var idx = new List<uint>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            uint s = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, a, n); AddVert(v, b, n); AddVert(v, c, n); AddVert(v, d, n);
            idx.AddRange(new uint[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }
        float h = 0.5f;
        Quad(new(-h, -h, h), new(h, -h, h), new(h, h, h), new(-h, h, h), Vector3.UnitZ);
        Quad(new(h, -h, -h), new(-h, -h, -h), new(-h, h, -h), new(h, h, -h), -Vector3.UnitZ);
        Quad(new(-h, -h, -h), new(-h, -h, h), new(-h, h, h), new(-h, h, -h), -Vector3.UnitX);
        Quad(new(h, -h, h), new(h, -h, -h), new(h, h, -h), new(h, h, h), Vector3.UnitX);
        Quad(new(-h, h, h), new(h, h, h), new(h, h, -h), new(-h, h, -h), Vector3.UnitY);
        Quad(new(-h, -h, -h), new(h, -h, -h), new(h, -h, h), new(-h, -h, h), -Vector3.UnitY);
        return UploadIndexedMesh(v.ToArray(), idx.ToArray());
    }

    private GpuMesh BuildUnitCylinderMesh(int segs)
    {
        var v = new List<float>();
        var idx = new List<uint>();
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * MathF.PI * 2f / segs;
            float a1 = (i + 1) * MathF.PI * 2f / segs;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
            float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
            uint b = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(c0, -0.5f, s0), new(c0, 0, s0));
            AddVert(v, new(c1, -0.5f, s1), new(c1, 0, s1));
            AddVert(v, new(c1, 0.5f, s1), new(c1, 0, s1));
            AddVert(v, new(c0, 0.5f, s0), new(c0, 0, s0));
            idx.AddRange(new uint[] { b, b + 1, b + 2, b, b + 2, b + 3 });

            uint t = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(0, 0.5f, 0), Vector3.UnitY);
            AddVert(v, new(c0, 0.5f, s0), Vector3.UnitY);
            AddVert(v, new(c1, 0.5f, s1), Vector3.UnitY);
            idx.AddRange(new uint[] { t, t + 1, t + 2 });
        }
        return UploadIndexedMesh(v.ToArray(), idx.ToArray());
    }

    private GpuMesh BuildUnitRingMesh(int segs, float innerR, float outerR)
    {
        var v = new List<float>();
        var idx = new List<uint>();
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * MathF.PI * 2f / segs;
            float a1 = (i + 1) * MathF.PI * 2f / segs;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
            float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
            uint b = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(c0 * innerR, 0, s0 * innerR), Vector3.UnitY);
            AddVert(v, new(c0 * outerR, 0, s0 * outerR), Vector3.UnitY);
            AddVert(v, new(c1 * outerR, 0, s1 * outerR), Vector3.UnitY);
            AddVert(v, new(c1 * innerR, 0, s1 * innerR), Vector3.UnitY);
            idx.AddRange(new uint[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        return UploadIndexedMesh(v.ToArray(), idx.ToArray());
    }

    private GpuMesh BuildGroundGridLines()
    {
        var v = new List<float>();
        for (int i = -12; i <= 12; i += 2)
        {
            AddVert(v, new(i, -0.01f, -9), Vector3.UnitY);
            AddVert(v, new(i, -0.01f, 9), Vector3.UnitY);
        }
        for (int j = -8; j <= 8; j += 2)
        {
            AddVert(v, new(-12, -0.01f, j), Vector3.UnitY);
            AddVert(v, new(12, -0.01f, j), Vector3.UnitY);
        }
        return UploadLineMesh(v.ToArray());
    }

    private GpuMesh BuildOrthogonalPipesMesh()
    {
        var v = new List<float>();
        var idx = new List<uint>();
        const int sides = 8;

        foreach (var route in Plant3DSceneData.PipeRoutes)
        {
            var col = Plant3DSceneData.GetMediumColor(route.Medium);
            float cr = 0.50f + (col.R / 255f) * 0.35f;
            float cg = 0.56f + (col.G / 255f) * 0.35f;
            float cb = 0.64f + (col.B / 255f) * 0.35f;

            for (int w = 0; w < route.Waypoints.Length - 1; w++)
            {
                var a = route.Waypoints[w];
                var b = route.Waypoints[w + 1];
                var dir = b - a;
                float len = dir.Length();
                if (len < 1e-4f) continue;
                dir /= len;

                var up = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.92f ? Vector3.UnitX : Vector3.UnitY;
                var right = Vector3.Normalize(Vector3.Cross(dir, up));
                var orthoUp = Vector3.Normalize(Vector3.Cross(right, dir));

                for (int s = 0; s < sides; s++)
                {
                    float a0 = s * MathF.PI * 2f / sides;
                    float a1 = (s + 1) * MathF.PI * 2f / sides;
                    var n0 = right * MathF.Cos(a0) + orthoUp * MathF.Sin(a0);
                    var n1 = right * MathF.Cos(a1) + orthoUp * MathF.Sin(a1);

                    uint baseIdx = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
                    AddVert(v, a + n0 * route.Radius, n0, cr, cg, cb, 0.72f, 0.32f);
                    AddVert(v, a + n1 * route.Radius, n1, cr, cg, cb, 0.72f, 0.32f);
                    AddVert(v, b + n1 * route.Radius, n1, cr, cg, cb, 0.72f, 0.32f);
                    AddVert(v, b + n0 * route.Radius, n0, cr, cg, cb, 0.72f, 0.32f);
                    idx.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2, baseIdx, baseIdx + 2, baseIdx + 3 });
                }
            }
        }

        return UploadIndexedMesh(v.ToArray(), idx.ToArray());
    }
}
