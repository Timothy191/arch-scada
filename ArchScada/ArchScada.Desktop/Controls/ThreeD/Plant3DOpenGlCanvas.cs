using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls.ThreeD;

/// <summary>
/// Hardware-accelerated OpenGL 3.3 Core / OpenGL ES 3.0 viewport for the CHPP 3D Digital Twin.
/// Renders Blender-exported .glb meshes, procedural industrial conveyors with trestles/chutes/pulleys,
/// 4-stage articulated wheel loader, jaw &amp; double-roll crusher mechanics, live tank fill levels,
/// coupled process flows, alarm halos, and selection/hover highlights.
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
    private const int GL_RGBA = 0x1908;
    private const int GL_UNSIGNED_BYTE = 0x1401;
    private const int GL_PACK_ALIGNMENT = 0x0D05;
    private const int GL_MULTISAMPLE = 0x809D;

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
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindAttribLocationDel(int program, uint index, byte* name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlLinkProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetProgramivDel(int program, int pname, int* param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUseProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlGetUniformLocationDel(int program, byte* name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniformMatrix4fvDel(int loc, int count, byte transpose, float* value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform3fDel(int loc, float x, float y, float z);
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
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlReadPixelsDel(int x, int y, int width, int height, int format, int type, void* pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlPixelStoreiDel(int pname, int param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlFinishDel();

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
    private GlBindAttribLocationDel? _glBindAttribLocation;
    private GlLinkProgramDel? _glLinkProgram;
    private GlGetProgramivDel? _glGetProgramiv;
    private GlUseProgramDel? _glUseProgram;
    private GlDeleteProgramDel? _glDeleteProgram;
    private GlGetUniformLocationDel? _glGetUniformLocation;
    private GlUniformMatrix4fvDel? _glUniformMatrix4fv;
    private GlUniform3fDel? _glUniform3f;
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
    private GlReadPixelsDel? _glReadPixels;
    private GlPixelStoreiDel? _glPixelStorei;
    private GlFinishDel? _glFinish;

    private readonly object _captureLock = new();
    private byte[]? _lastFrameRgba;
    private int _lastFrameWidth;
    private int _lastFrameHeight;

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
    private GpuMesh? _unitCone;
    private GpuMesh? _gridLines;
    private GpuMesh? _pipesMesh;
    private GpuMesh? _conveyorStructureMesh;

    private int _program;
    private int _uModel = -1;
    private int _uViewProj = -1;
    private int _uCamPos = -1;
    private int _uTint = -1;
    private int _uTintMix = -1;
    private int _uAlpha = -1;
    private int _uUnlit = -1;

    public double CameraYaw { get; set; } = -0.58;
    public double CameraPitch { get; set; } = 0.56;
    public double CameraZoom { get; set; } = 0.68;
    public double TargetX { get; set; } = -2.0;
    public double TargetZ { get; set; } = -3.0;
    public double SimTime { get; set; }
    public IReadOnlyList<EquipmentItem>? Equipment { get; set; }
    public Unity3DEffectSettings? Effects { get; set; }
    public string? SelectedEquipmentId { get; set; }
    public string? SelectedNodeId { get; set; }
    public string? HoveredNodeId { get; set; }

    public bool IsHardwareActive { get; private set; }
    public int LoadedGlbCount => _glbGpuMeshes.Count;
    public volatile bool CaptureRequested;

    public WriteableBitmap? CaptureLastFrameBitmap()
    {
        byte[]? rgba;
        int w;
        int h;
        lock (_captureLock)
        {
            if (_lastFrameRgba is null || _lastFrameWidth <= 0 || _lastFrameHeight <= 0)
                return null;
            rgba = (byte[])_lastFrameRgba.Clone();
            w = _lastFrameWidth;
            h = _lastFrameHeight;
        }

        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Avalonia.Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Opaque);

        using var fb = bmp.Lock();
        int rowBytes = w * 4;
        for (int y = 0; y < h; y++)
        {
            int srcOffset = (h - 1 - y) * rowBytes;
            IntPtr dstRow = IntPtr.Add(fb.Address, y * fb.RowBytes);
            Marshal.Copy(rgba, srcOffset, dstRow, rowBytes);
        }

        return bmp;
    }

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
            _glBindAttribLocation = LoadProc<GlBindAttribLocationDel>(gl, "glBindAttribLocation");
            _glLinkProgram = LoadProc<GlLinkProgramDel>(gl, "glLinkProgram");
            _glGetProgramiv = LoadProc<GlGetProgramivDel>(gl, "glGetProgramiv");
            _glUseProgram = LoadProc<GlUseProgramDel>(gl, "glUseProgram");
            _glDeleteProgram = LoadProc<GlDeleteProgramDel>(gl, "glDeleteProgram");
            _glGetUniformLocation = LoadProc<GlGetUniformLocationDel>(gl, "glGetUniformLocation");
            _glUniformMatrix4fv = LoadProc<GlUniformMatrix4fvDel>(gl, "glUniformMatrix4fv");
            _glUniform3f = LoadProc<GlUniform3fDel>(gl, "glUniform3f");
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
            _glReadPixels = LoadProc<GlReadPixelsDel>(gl, "glReadPixels");
            _glPixelStorei = LoadProc<GlPixelStoreiDel>(gl, "glPixelStorei");
            _glFinish = LoadProc<GlFinishDel>(gl, "glFinish");

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

            foreach (var (fileName, meshData) in Plant3DSceneData.LoadAllGlbMeshes())
            {
                _glbGpuMeshes[fileName] = UploadIndexedMesh(meshData.Vertices, meshData.Indices);
            }

            _unitBox = BuildUnitBoxMesh();
            _unitCylinder = BuildUnitCylinderMesh(20);
            _unitRing = BuildUnitRingMesh(32, 0.88f, 1.0f);
            _unitCone = BuildUnitConeMesh(14);
            _gridLines = BuildGroundGridLines();
            _pipesMesh = BuildOrthogonalPipesMesh();
            _conveyorStructureMesh = BuildConveyorStructuresMesh();

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
        DeleteMesh(_unitCone); _unitCone = null;
        DeleteMesh(_gridLines); _gridLines = null;
        DeleteMesh(_pipesMesh); _pipesMesh = null;
        DeleteMesh(_conveyorStructureMesh); _conveyorStructureMesh = null;

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
        _glEnable!(GL_MULTISAMPLE);
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

        var flowMap = Plant3DSceneData.EvaluateProcessFlows(eqMap, SimTime);
        eqMap.TryGetValue("AF-001", out var afEq);
        bool loaderActive = fx.AnimateConveyors && (afEq is null || afEq.Status is not (EquipmentStatus.Stopped or EquipmentStatus.Fault or EquipmentStatus.Offline));
        var loaderPose = Plant3DSceneData.ComputeLoaderPose(SimTime, loaderActive);

        // 1. Ground Grid
        if (_gridLines is not null)
        {
            SetMatrix(_uModel, Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.22f, 0.65f, 0.92f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.20f);
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
            _glUniform1f!(_uAlpha, 0.50f);
            foreach (var p in Plant3DSceneData.MachineNodes)
            {
                var pos = p.Id == "FEL-101" ? loaderPose.ChassisPosition : p.Position;
                float rx = p.HalfExtents.X * 1.05f;
                float rz = p.HalfExtents.Z * 1.05f;
                var sm = Matrix4x4.CreateScale(rx, 0.02f, rz)
                       * Matrix4x4.CreateTranslation(pos.X + 0.18f, 0.015f, pos.Z + 0.18f);
                SetMatrix(_uModel, sm);
                DrawMesh(_unitCylinder);
            }
            _glDepthMask!(1);
        }

        // 4. Orthogonal 3D Slurry / Magnetite / Water Piping
        if (_pipesMesh is not null)
        {
            SetMatrix(_uModel, Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.55f, 0.64f, 0.75f);
            _glUniform1f!(_uTintMix, 0.25f);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);
            DrawMesh(_pipesMesh);
        }

        // 5. Procedural Industrial Conveyor Structures (trestles, stringers, walkways, handrails, transfer chutes)
        if (_conveyorStructureMesh is not null)
        {
            SetMatrix(_uModel, Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.45f, 0.54f, 0.64f);
            _glUniform1f!(_uTintMix, 0.05f);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);
            DrawMesh(_conveyorStructureMesh);
        }

        // 6. All 18 Machine Nodes (Blender .glb meshes + mechanical animations)
        string? activeSelected = SelectedNodeId ?? SelectedEquipmentId;
        foreach (var p in Plant3DSceneData.MachineNodes)
        {
            eqMap.TryGetValue(p.LinkedScadaId, out var eq);
            var flow = flowMap.GetValueOrDefault(p.Id);
            bool running = flow is not null ? flow.IsRunning : (eq is null || eq.Status != EquipmentStatus.Stopped);

            var tint = Plant3DSceneData.GetStatusTint(p, eq, fx);
            bool isSelected = string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase);
            bool isHovered = string.Equals(HoveredNodeId, p.Id, StringComparison.OrdinalIgnoreCase);

            float tintMix = fx.ShowThermalHeatmap ? 0.78f
                : (eq?.Status is EquipmentStatus.Fault or EquipmentStatus.Warning or EquipmentStatus.Stopped ? 0.58f : 0.08f);
            if (isSelected) { tint = Color.Parse("#38BDF8"); tintMix = MathF.Max(tintMix, 0.30f); }
            else if (isHovered) { tint = Color.Parse("#7DD3FC"); tintMix = MathF.Max(tintMix, 0.20f); }

            if (p.Id == "FEL-101")
            {
                RenderWheelLoader(loaderPose, tint, tintMix);
                continue;
            }

            float vibY = 0f;
            if (p.Vibrates && fx.EnableVibrationPhysics && running)
            {
                float load = eq is not null ? (float)(eq.LoadPercent / 100.0) : 0.7f;
                vibY = MathF.Sin((float)(SimTime * 28.0)) * (0.018f + 0.024f * load);
            }

            var model = Matrix4x4.CreateScale(p.Scale)
                      * Matrix4x4.CreateRotationY(p.YawDegrees * (MathF.PI / 180f))
                      * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + vibY, p.Position.Z);
            SetMatrix(_uModel, model);

            _glUniform3f!(_uTint, tint.R / 255f, tint.G / 255f, tint.B / 255f);
            _glUniform1f!(_uTintMix, tintMix);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);

            if (_glbGpuMeshes.TryGetValue(p.GlbFileName, out var gpuMesh))
            {
                DrawMesh(gpuMesh);
            }
            else if (p.EquipmentType.Contains("Stockpile", StringComparison.OrdinalIgnoreCase) && _unitCone is not null)
            {
                var coneMat = Matrix4x4.CreateScale(p.HalfExtents.X, p.HalfExtents.Y * 2f, p.HalfExtents.Z)
                            * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.HalfExtents.Y, p.Position.Z);
                SetMatrix(_uModel, coneMat);
                _glUniform1f!(_uTintMix, 0.85f);
                DrawMesh(_unitCone);
            }
            else if (_unitBox is not null)
            {
                var fbMat = Matrix4x4.CreateScale(p.HalfExtents.X * 2f, p.HalfExtents.Y * 2f, p.HalfExtents.Z * 2f)
                          * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.HalfExtents.Y + vibY, p.Position.Z);
                SetMatrix(_uModel, fbMat);
                _glUniform1f!(_uTintMix, 0.75f);
                DrawMesh(_unitBox);
            }

            // Mechanical sub-animations
            if (p.Id == "JC-101")
            {
                RenderJawCrusherMechanics(p.Position, running && fx.AnimateConveyors);
            }
            else if (p.Id == "DRC-101")
            {
                RenderDoubleRollCrusherMechanics(p.Position, running && fx.AnimateConveyors);
            }
            else if (p.HasRotatingTop && _unitBox is not null)
            {
                float ang = (fx.AnimateConveyors && running) ? (float)(SimTime * (p.Id == "TK-201" ? 0.45 : 1.8)) : 0.2f;
                var rotMat = Matrix4x4.CreateScale(p.RotatingTopLength, 0.14f, 0.24f)
                           * Matrix4x4.CreateRotationY(ang)
                           * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.RotatingTopY, p.Position.Z);
                SetMatrix(_uModel, rotMat);
                _glUniform3f!(_uTint, 0.22f, 0.78f, 0.98f);
                _glUniform1f!(_uTintMix, 1.0f);
                DrawMesh(_unitBox);
            }
        }

        // 7. Dynamic Conveyor Pulleys, Moving Belt Cleats, Continuous Coal Stream & Flow Chevrons
        RenderDynamicConveyors(flowMap, fx, activeSelected, HoveredNodeId);

        // 8. Live 3D Fill-Level Indicators (BN-101, BN-201, TK-301, TK-201)
        if (_unitCylinder is not null)
        {
            _glDepthMask!(0);
            _glUniform1f!(_uUnlit, 0.2f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.44f);

            foreach (var p in Plant3DSceneData.MachineNodes)
            {
                if (!p.HasFillLevel) continue;
                eqMap.TryGetValue(p.LinkedScadaId, out var eq);
                float fillFrac = eq is not null ? Math.Clamp((float)(eq.LoadPercent / 100.0), 0.12f, 0.95f) : 0.60f;
                if (p.Id == "BN-101") fillFrac = Math.Clamp(fillFrac + loaderPose.BinFillPulse, 0.15f, 0.98f);

                float fillH = p.FillMaxHeight * fillFrac;
                var fillColor = p.Id == "BN-101" ? Color.Parse("#F59E0B")
                              : p.Id == "BN-201" ? Color.Parse("#60A5FA")
                              : p.Id == "TK-301" ? Color.Parse("#14B8A6")
                              : Color.Parse("#38BDF8");

                var fm = Matrix4x4.CreateScale(p.FillRadius, fillH, p.FillRadius)
                       * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.FillBaseY + fillH * 0.5f, p.Position.Z);
                SetMatrix(_uModel, fm);
                _glUniform3f!(_uTint, fillColor.R / 255f, fillColor.G / 255f, fillColor.B / 255f);
                DrawMesh(_unitCylinder);
            }
            _glDepthMask!(1);
        }

        // 9. Telemetry-Coupled Slurry/Magnetite/Water Pipe Flow Particles
        if (fx.ShowFlowParticles && _unitBox is not null)
        {
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 0.95f);

            foreach (var route in Plant3DSceneData.PipeRoutes)
            {
                eqMap.TryGetValue(route.SourceEquipmentId, out var srcEq);
                double speed = Plant3DSceneData.GetFlowSpeedFactor(srcEq);
                if (speed <= 0.0) continue;

                var c = Plant3DSceneData.GetMediumColor(route.Medium);
                _glUniform3f!(_uTint, c.R / 255f, c.G / 255f, c.B / 255f);

                const int count = 6;
                for (int i = 0; i < count; i++)
                {
                    float t = (float)((SimTime * 0.30 * speed + (double)i / count) % 1.0);
                    var pos = Plant3DSceneData.SamplePolyline(route.Waypoints, t);
                    var pm = Matrix4x4.CreateScale(0.16f) * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.06f, pos.Z);
                    SetMatrix(_uModel, pm);
                    DrawMesh(_unitBox);
                }
            }
        }

        // 10. Alarm Halos & Selection / Hover Rings across ALL 29 nodes
        if (_unitRing is not null)
        {
            _glDepthMask!(0);
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform1f!(_uTintMix, 1.0f);

            float pulse = 0.55f + 0.45f * MathF.Sin((float)(SimTime * 4.5));
            foreach (var p in Plant3DSceneData.AllNodes)
            {
                eqMap.TryGetValue(p.LinkedScadaId, out var eq);
                var pos = p.Id == "FEL-101" ? loaderPose.ChassisPosition : p.Position;
                float baseR = p.IsConveyor ? 1.15f : MathF.Max(p.HalfExtents.X, p.HalfExtents.Z) + 0.32f;

                if (!p.IsConveyor && fx.ShowAlarmHalos && eq is not null && eq.Status is EquipmentStatus.Warning or EquipmentStatus.Fault)
                {
                    var ac = eq.Status == EquipmentStatus.Fault ? Color.Parse("#EF4444") : Color.Parse("#F59E0B");
                    var rm = Matrix4x4.CreateScale(baseR + 0.25f * pulse, 1f, baseR + 0.25f * pulse)
                           * Matrix4x4.CreateTranslation(pos.X, 0.08f, pos.Z);
                    SetMatrix(_uModel, rm);
                    _glUniform3f!(_uTint, ac.R / 255f, ac.G / 255f, ac.B / 255f);
                    _glUniform1f!(_uAlpha, 0.42f + 0.42f * pulse);
                    DrawMesh(_unitRing);
                }

                bool isSel = string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase);
                bool isHov = string.Equals(HoveredNodeId, p.Id, StringComparison.OrdinalIgnoreCase);
                if (isSel || isHov)
                {
                    var sc = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
                    var rm = Matrix4x4.CreateScale(baseR + 0.16f, 1f, baseR + 0.16f)
                           * Matrix4x4.CreateTranslation(pos.X, p.IsConveyor ? pos.Y : 0.10f, pos.Z);
                    SetMatrix(_uModel, rm);
                    _glUniform3f!(_uTint, sc.R / 255f, sc.G / 255f, sc.B / 255f);
                    _glUniform1f!(_uAlpha, isSel ? 0.92f : 0.62f);
                    DrawMesh(_unitRing);
                }
            }
            _glDepthMask!(1);
        }

        if (CaptureRequested && _glReadPixels is not null && pw > 0 && ph > 0)
        {
            _glFinish?.Invoke();
            _glPixelStorei?.Invoke(GL_PACK_ALIGNMENT, 1);
            var buf = new byte[pw * ph * 4];
            fixed (byte* pBuf = buf)
            {
                _glReadPixels(0, 0, pw, ph, GL_RGBA, GL_UNSIGNED_BYTE, pBuf);
            }
            lock (_captureLock)
            {
                _lastFrameRgba = buf;
                _lastFrameWidth = pw;
                _lastFrameHeight = ph;
            }
        }

        _glBindVertexArray!(0);
        _glUseProgram!(0);
    }

    private void RenderWheelLoader(LoaderPose pose, Color tint, float tintMix)
    {
        const float deg = MathF.PI / 180f;
        var chassisWorld = Matrix4x4.CreateRotationZ(pose.PitchDegrees * deg)
                         * Matrix4x4.CreateRotationY(pose.YawDegrees * deg)
                         * Matrix4x4.CreateTranslation(pose.ChassisPosition);

        SetMatrix(_uModel, chassisWorld);
        _glUniform3f!(_uTint, tint.R / 255f, tint.G / 255f, tint.B / 255f);
        _glUniform1f!(_uTintMix, tintMix);
        _glUniform1f!(_uAlpha, 1.0f);
        _glUniform1f!(_uUnlit, 0.0f);

        if (_glbGpuMeshes.TryGetValue("wheel_loader_chassis.glb", out var chassisMesh))
        {
            DrawMesh(chassisMesh);
        }
        else if (_unitBox is not null)
        {
            var bodyMat = Matrix4x4.CreateScale(2.3f, 1.05f, 1.45f) * Matrix4x4.CreateTranslation(-0.15f, 0.85f, 0f) * chassisWorld;
            SetMatrix(_uModel, bodyMat);
            _glUniform1f!(_uTintMix, 0.85f);
            DrawMesh(_unitBox);

            var cabMat = Matrix4x4.CreateScale(0.95f, 0.85f, 1.15f) * Matrix4x4.CreateTranslation(-0.35f, 1.72f, 0f) * chassisWorld;
            SetMatrix(_uModel, cabMat);
            _glUniform3f!(_uTint, 0.15f, 0.22f, 0.30f);
            DrawMesh(_unitBox);

            if (_unitCylinder is not null)
            {
                _glUniform3f!(_uTint, 0.09f, 0.11f, 0.14f);
                foreach (float wx in new[] { -0.85f, 0.72f })
                foreach (float wz in new[] { -0.78f, 0.78f })
                {
                    var wMat = Matrix4x4.CreateScale(0.52f, 0.34f, 0.52f)
                             * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                             * Matrix4x4.CreateTranslation(wx, 0.52f, wz)
                             * chassisWorld;
                    SetMatrix(_uModel, wMat);
                    DrawMesh(_unitCylinder);
                }
            }
        }

        // Articulated Boom + Bucket assembly (wheel_loader_bucket.glb origin is at chassis boom pivot (1.30, 1.38, 0))
        var boomPivot = Matrix4x4.CreateRotationZ(pose.BoomAngleDegrees * deg)
                      * Matrix4x4.CreateTranslation(1.30f, 1.38f, 0f);
        var glbBucketWorld = Matrix4x4.CreateRotationZ((pose.BoomAngleDegrees + pose.BucketDumpAngleDegrees * 0.45f) * deg)
                           * Matrix4x4.CreateTranslation(1.30f, 1.38f, 0f)
                           * chassisWorld;

        if (_glbGpuMeshes.TryGetValue("wheel_loader_bucket.glb", out var bucketMesh))
        {
            SetMatrix(_uModel, glbBucketWorld);
            _glUniform3f!(_uTint, tint.R / 255f, tint.G / 255f, tint.B / 255f);
            _glUniform1f!(_uTintMix, tintMix);
            DrawMesh(bucketMesh);
        }
        else if (_unitBox is not null)
        {
            var bucketLocal = Matrix4x4.CreateRotationZ(pose.BucketDumpAngleDegrees * deg)
                            * Matrix4x4.CreateTranslation(1.55f, -0.75f, 0f)
                            * boomPivot;
            var bucketWorld = bucketLocal * chassisWorld;
            foreach (float bz in new[] { -0.52f, 0.52f })
            {
                var armMat = Matrix4x4.CreateScale(1.75f, 0.16f, 0.12f)
                           * Matrix4x4.CreateRotationZ(-0.45f)
                           * Matrix4x4.CreateTranslation(0.75f, -0.35f, bz)
                           * boomPivot * chassisWorld;
                SetMatrix(_uModel, armMat);
                _glUniform3f!(_uTint, 0.92f, 0.68f, 0.05f);
                _glUniform1f!(_uTintMix, 1.0f);
                DrawMesh(_unitBox);
            }

            var scoopMat = Matrix4x4.CreateScale(0.85f, 0.58f, 1.85f)
                         * Matrix4x4.CreateTranslation(0.35f, -0.10f, 0f)
                         * bucketWorld;
            SetMatrix(_uModel, scoopMat);
            _glUniform3f!(_uTint, 0.25f, 0.30f, 0.38f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawMesh(_unitBox);
        }

        // Visible coal boulder dump stream into BN-101 during [0.32, 0.48]
        if (pose.IsDumpingCoal && _unitBox is not null)
        {
            var dumpOrigin = Vector3.Transform(new Vector3(2.45f, -0.95f, 0f), glbBucketWorld);
            var binTarget = new Vector3(-13.8f, 2.15f, 8.8f);
            _glUniform3f!(_uTint, 0.72f, 0.45f, 0.08f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uUnlit, 0.25f);
            for (int i = 0; i < 7; i++)
            {
                float frac = (float)((SimTime * 2.8 + i / 7.0) % 1.0);
                var p = Vector3.Lerp(dumpOrigin, binTarget, frac);
                p.X += MathF.Sin(i * 2.1f) * 0.22f;
                p.Z += MathF.Cos(i * 1.7f) * 0.22f;
                p.Y -= frac * frac * 0.35f;
                var bm = Matrix4x4.CreateScale(0.22f - 0.04f * (i % 2))
                       * Matrix4x4.CreateRotationY(i * 0.8f + (float)SimTime * 3f)
                       * Matrix4x4.CreateTranslation(p);
                SetMatrix(_uModel, bm);
                DrawMesh(_unitBox);
            }
            _glUniform1f!(_uUnlit, 0.0f);
        }
    }

    private void RenderJawCrusherMechanics(Vector3 pos, bool running)
    {
        if (_unitCylinder is null || _unitBox is null) return;
        float flywheelAngle = running ? (float)(SimTime * 6.5) : 0f;

        // Dual side flywheels mounted on eccentric shaft bosses (Y=2.50m, Z=+-1.06m)
        foreach (float zOff in new[] { -1.06f, 1.06f })
        {
            var wheelCenter = Matrix4x4.CreateRotationY(flywheelAngle)
                            * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                            * Matrix4x4.CreateTranslation(pos.X, pos.Y + 2.50f, pos.Z + zOff);
            var wheelMat = Matrix4x4.CreateScale(0.58f, 0.18f, 0.58f) * wheelCenter;
            SetMatrix(_uModel, wheelMat);
            _glUniform3f!(_uTint, 0.85f, 0.55f, 0.10f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawMesh(_unitCylinder);

            // Spoke bar to make rotation unmistakable
            var spokeMat = Matrix4x4.CreateScale(1.08f, 0.20f, 0.12f) * wheelCenter;
            SetMatrix(_uModel, spokeMat);
            _glUniform3f!(_uTint, 0.18f, 0.24f, 0.32f);
            DrawMesh(_unitBox);
        }

        // Reciprocating corrugated swing jaw plate inside open throat (~4.5 Hz)
        float jawSwing = running ? MathF.Sin((float)(SimTime * 28.27)) * 0.08f : 0f;
        var jawMat = Matrix4x4.CreateScale(0.16f, 1.05f, 1.16f)
                   * Matrix4x4.CreateRotationZ(-0.20f + jawSwing * 1.4f)
                   * Matrix4x4.CreateTranslation(pos.X - 0.12f + jawSwing, pos.Y + 2.48f, pos.Z);
        SetMatrix(_uModel, jawMat);
        _glUniform3f!(_uTint, 0.92f, 0.68f, 0.18f);
        _glUniform1f!(_uTintMix, 1.0f);
        DrawMesh(_unitBox);
    }

    private void RenderDoubleRollCrusherMechanics(Vector3 pos, bool running)
    {
        if (_unitCylinder is null || _unitBox is null) return;
        float rollAngle = running ? (float)(SimTime * 5.2) : 0f;

        // Two counter-rotating toothed horizontal rollers at bearing bosses (X=+-0.36m, Y=2.02m, axis along Z)
        for (int r = 0; r < 2; r++)
        {
            float xOff = r == 0 ? -0.36f : 0.36f;
            float dir = r == 0 ? 1f : -1f; // Counter-rotating!
            float ang = rollAngle * dir;

            var rollAxis = Matrix4x4.CreateRotationY(ang)
                         * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                         * Matrix4x4.CreateTranslation(pos.X + xOff, pos.Y + 2.02f, pos.Z);

            var drumMat = Matrix4x4.CreateScale(0.32f, 1.32f, 0.32f) * rollAxis;
            SetMatrix(_uModel, drumMat);
            _glUniform3f!(_uTint, 0.35f, 0.42f, 0.52f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawMesh(_unitCylinder);

            // Visible toothed crusher ribs
            _glUniform3f!(_uTint, 0.96f, 0.65f, 0.12f);
            for (int tooth = 0; tooth < 4; tooth++)
            {
                float ta = ang + tooth * (MathF.PI * 0.25f);
                var ribMat = Matrix4x4.CreateScale(0.72f, 1.28f, 0.09f)
                           * Matrix4x4.CreateRotationY(ta)
                           * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                           * Matrix4x4.CreateTranslation(pos.X + xOff, pos.Y + 2.02f, pos.Z);
                SetMatrix(_uModel, ribMat);
                DrawMesh(_unitBox);
            }
        }
    }

    private void RenderDynamicConveyors(
        IReadOnlyDictionary<string, ProcessFlowState> flowMap,
        Unity3DEffectSettings fx,
        string? activeSelected,
        string? hoveredId)
    {
        if (_unitBox is null || _unitCylinder is null) return;

        foreach (var c in Plant3DSceneData.Conveyors)
        {
            var flow = flowMap.GetValueOrDefault(c.Id);
            double speed = (fx.AnimateConveyors && flow is not null) ? flow.NormalizedRate : 0.0;
            bool isSel = string.Equals(activeSelected, c.Id, StringComparison.OrdinalIgnoreCase);
            bool isHov = string.Equals(hoveredId, c.Id, StringComparison.OrdinalIgnoreCase);

            var delta = c.HeadPos - c.TailPos;
            float length = delta.Length();
            if (length < 0.1f) continue;

            var dir = delta / length;
            float yaw = MathF.Atan2(-dir.Z, dir.X);
            float pitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
            var beltRot = Matrix4x4.CreateRotationZ(pitch) * Matrix4x4.CreateRotationY(yaw);

            // Selection / hover highlight strip along belt edge
            if (isSel || isHov)
            {
                var hlColor = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
                var mid = (c.TailPos + c.HeadPos) * 0.5f;
                var hlMat = Matrix4x4.CreateScale(length, 0.08f, c.Width + 0.28f)
                          * beltRot
                          * Matrix4x4.CreateTranslation(mid.X, mid.Y + 0.04f, mid.Z);
                SetMatrix(_uModel, hlMat);
                _glUniform3f!(_uTint, hlColor.R / 255f, hlColor.G / 255f, hlColor.B / 255f);
                _glUniform1f!(_uTintMix, 1.0f);
                _glUniform1f!(_uAlpha, 0.85f);
                _glUniform1f!(_uUnlit, 1.0f);
                DrawMesh(_unitBox);
                _glUniform1f!(_uAlpha, 1.0f);
                _glUniform1f!(_uUnlit, 0.0f);
            }

            // Rotating Tail & Head Pulleys
            float pulleyAng = (float)(SimTime * speed * 4.5);
            foreach (var endPos in new[] { c.TailPos, c.HeadPos })
            {
                var pOrient = Matrix4x4.CreateRotationY(pulleyAng)
                            * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                            * Matrix4x4.CreateRotationY(yaw)
                            * Matrix4x4.CreateTranslation(endPos.X, endPos.Y - 0.04f, endPos.Z);
                var pScale = Matrix4x4.CreateScale(0.24f, c.Width * 1.12f, 0.24f) * pOrient;
                SetMatrix(_uModel, pScale);
                _glUniform3f!(_uTint, 0.88f, 0.48f, 0.08f);
                _glUniform1f!(_uTintMix, 1.0f);
                DrawMesh(_unitCylinder);

                // Cross-key bar on pulley to make rotation visible
                var keyMat = Matrix4x4.CreateScale(0.50f, c.Width * 1.15f, 0.07f) * pOrient;
                SetMatrix(_uModel, keyMat);
                _glUniform3f!(_uTint, 0.16f, 0.22f, 0.30f);
                DrawMesh(_unitBox);
            }

            // Moving Belt Cleats
            const float cleatSpacing = 0.72f;
            int cleatCount = Math.Max(3, (int)(length / cleatSpacing));
            float phase = (float)((SimTime * speed * 1.45) % cleatSpacing);
            _glUniform3f!(_uTint, 0.32f, 0.38f, 0.46f);
            _glUniform1f!(_uTintMix, 1.0f);
            for (int i = 0; i < cleatCount; i++)
            {
                float distAlong = i * cleatSpacing + phase;
                if (distAlong > length) continue;
                var p = c.TailPos + dir * distAlong;
                var cm = Matrix4x4.CreateScale(0.08f, 0.05f, c.Width * 0.88f)
                       * beltRot
                       * Matrix4x4.CreateTranslation(p.X, p.Y + 0.08f, p.Z);
                SetMatrix(_uModel, cm);
                DrawMesh(_unitBox);
            }

            // Continuous Coal Stream (along feed chute -> belt -> head transfer chute)
            if (flow is not null && flow.NormalizedRate > 0.02)
            {
                var matCol = Plant3DSceneData.GetMaterialColor(c.MaterialClass);
                float lumpSize = Plant3DSceneData.GetMaterialLumpSize(c.MaterialClass);
                var fullPts = c.GetFullFlowWaypoints();
                float totalLen = 0f;
                for (int i = 0; i < fullPts.Length - 1; i++)
                    totalLen += Vector3.Distance(fullPts[i], fullPts[i + 1]);

                int parcelCount = Math.Max(8, (int)(totalLen / 0.55f));
                _glUniform3f!(_uTint, matCol.R / 255f, matCol.G / 255f, matCol.B / 255f);
                _glUniform1f!(_uTintMix, 1.0f);

                for (int i = 0; i < parcelCount; i++)
                {
                    float t = (float)((SimTime * 0.24 * Math.Max(0.2, speed) + (double)i / parcelCount) % 1.0);
                    var pos = Plant3DSceneData.SamplePolyline(fullPts, t);
                    float jitter = MathF.Sin(i * 3.1f) * (c.Width * 0.16f);
                    var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
                    pos += right * jitter;
                    pos.Y += 0.14f;

                    var pm = Matrix4x4.CreateScale(lumpSize * 1.25f, lumpSize * 0.85f, lumpSize * 1.1f)
                           * Matrix4x4.CreateRotationY(i * 0.9f)
                           * Matrix4x4.CreateTranslation(pos);
                    SetMatrix(_uModel, pm);
                    DrawMesh(_unitBox);
                }

                // 3D Directional Flow Chevrons (>>>) when ShowFlowDirection is enabled
                if (fx.ShowFlowDirection && _unitCone is not null)
                {
                    _glUniform3f!(_uTint, 0.22f, 0.82f, 0.98f);
                    _glUniform1f!(_uUnlit, 1.0f);
                    _glUniform1f!(_uAlpha, 0.88f);
                    int arrowCount = Math.Max(2, (int)(length / 2.8f));
                    for (int a = 0; a < arrowCount; a++)
                    {
                        float t = (float)((SimTime * 0.35 * Math.Max(0.25, speed) + (double)a / arrowCount) % 1.0);
                        var ap = Vector3.Lerp(c.TailPos, c.HeadPos, t);
                        ap.Y += 0.65f;
                        // UnitCone points +Y; rotate -90 deg around Z to point +X along belt!
                        var am = Matrix4x4.CreateScale(0.22f, 0.42f, 0.22f)
                               * Matrix4x4.CreateRotationZ(-MathF.PI * 0.5f)
                               * beltRot
                               * Matrix4x4.CreateTranslation(ap);
                        SetMatrix(_uModel, am);
                        DrawMesh(_unitCone);
                    }
                    _glUniform1f!(_uUnlit, 0.0f);
                    _glUniform1f!(_uAlpha, 1.0f);
                }
            }
        }
    }

    private int BuildShaderProgram(bool isEs)
    {
        string header = isEs ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";

        string vsSrc = header + @"
in vec3 aPos;
in vec3 aNormal;
in vec3 aColor;
in vec2 aPbr;

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
    float rim = pow(1.0 - max(dot(N, V), 0.0), 3.0) * 0.18;

    vec3 lit = baseCol * (ambient + vec3(0.88, 0.90, 0.94) * ndotl) + vec3(spec) + vec3(0.22, 0.52, 0.85) * rim;

    float dist = length(uCamPos - vWorldPos);
    float fog = clamp((dist - 28.0) / 48.0, 0.0, 0.40);
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
        BindAttribLocation(prog, 0, "aPos");
        BindAttribLocation(prog, 1, "aNormal");
        BindAttribLocation(prog, 2, "aColor");
        BindAttribLocation(prog, 3, "aPbr");
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

    private void BindAttribLocation(int prog, uint index, string name)
    {
        if (_glBindAttribLocation is null) return;
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            _glBindAttribLocation(prog, index, p);
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

    private static void AppendBox(List<float> v, List<uint> idx, Matrix4x4 xform, float r, float g, float b, float met = 0.6f, float rough = 0.45f)
    {
        void Quad(Vector3 a, Vector3 b0, Vector3 c, Vector3 d, Vector3 n)
        {
            var wa = Vector3.Transform(a, xform);
            var wb = Vector3.Transform(b0, xform);
            var wc = Vector3.Transform(c, xform);
            var wd = Vector3.Transform(d, xform);
            var wn = Vector3.Normalize(Vector3.TransformNormal(n, xform));
            uint s = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, wa, wn, r, g, b, met, rough);
            AddVert(v, wb, wn, r, g, b, met, rough);
            AddVert(v, wc, wn, r, g, b, met, rough);
            AddVert(v, wd, wn, r, g, b, met, rough);
            idx.AddRange(new uint[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }
        const float h = 0.5f;
        Quad(new(-h, -h, h), new(h, -h, h), new(h, h, h), new(-h, h, h), Vector3.UnitZ);
        Quad(new(h, -h, -h), new(-h, -h, -h), new(-h, h, -h), new(h, h, -h), -Vector3.UnitZ);
        Quad(new(-h, -h, -h), new(-h, -h, h), new(-h, h, h), new(-h, h, -h), -Vector3.UnitX);
        Quad(new(h, -h, h), new(h, -h, -h), new(h, h, -h), new(h, h, h), Vector3.UnitX);
        Quad(new(-h, h, h), new(h, h, h), new(h, h, -h), new(-h, h, -h), Vector3.UnitY);
        Quad(new(-h, -h, -h), new(h, -h, -h), new(h, -h, h), new(-h, -h, h), -Vector3.UnitY);
    }

    private GpuMesh BuildUnitBoxMesh()
    {
        var v = new List<float>();
        var idx = new List<uint>();
        AppendBox(v, idx, Matrix4x4.Identity, 1f, 1f, 1f);
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

    private GpuMesh BuildUnitConeMesh(int segs)
    {
        var v = new List<float>();
        var idx = new List<uint>();
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * MathF.PI * 2f / segs;
            float a1 = (i + 1) * MathF.PI * 2f / segs;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
            float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
            var n0 = Vector3.Normalize(new Vector3(c0, 0.5f, s0));
            var n1 = Vector3.Normalize(new Vector3(c1, 0.5f, s1));

            uint b = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(0, 0.5f, 0), Vector3.Normalize(n0 + n1));
            AddVert(v, new(c0, -0.5f, s0), n0);
            AddVert(v, new(c1, -0.5f, s1), n1);
            idx.AddRange(new uint[] { b, b + 1, b + 2 });

            uint bc = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(0, -0.5f, 0), -Vector3.UnitY);
            AddVert(v, new(c1, -0.5f, s1), -Vector3.UnitY);
            AddVert(v, new(c0, -0.5f, s0), -Vector3.UnitY);
            idx.AddRange(new uint[] { bc, bc + 1, bc + 2 });
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
        for (int x = -18; x <= 14; x += 2)
        {
            AddVert(v, new(x, -0.01f, -18), Vector3.UnitY);
            AddVert(v, new(x, -0.01f, 16), Vector3.UnitY);
        }
        for (int z = -18; z <= 16; z += 2)
        {
            AddVert(v, new(-18, -0.01f, z), Vector3.UnitY);
            AddVert(v, new(14, -0.01f, z), Vector3.UnitY);
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
                AppendSegmentTube(v, idx, route.Waypoints[w], route.Waypoints[w + 1], route.Radius, sides, cr, cg, cb);
            }
        }

        return UploadIndexedMesh(v.ToArray(), idx.ToArray());
    }

    private static void AppendSegmentTube(List<float> v, List<uint> idx, Vector3 a, Vector3 b, float radius, int sides, float cr, float cg, float cb)
    {
        var dir = b - a;
        float len = dir.Length();
        if (len < 1e-4f) return;
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
            AddVert(v, a + n0 * radius, n0, cr, cg, cb, 0.72f, 0.32f);
            AddVert(v, a + n1 * radius, n1, cr, cg, cb, 0.72f, 0.32f);
            AddVert(v, b + n1 * radius, n1, cr, cg, cb, 0.72f, 0.32f);
            AddVert(v, b + n0 * radius, n0, cr, cg, cb, 0.72f, 0.32f);
            idx.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2, baseIdx, baseIdx + 2, baseIdx + 3 });
        }
    }

    private GpuMesh BuildConveyorStructuresMesh()
    {
        var v = new List<float>();
        var idx = new List<uint>();

        foreach (var c in Plant3DSceneData.Conveyors)
        {
            var delta = c.HeadPos - c.TailPos;
            float len = delta.Length();
            if (len < 0.1f) continue;

            var dir = delta / len;
            float yaw = MathF.Atan2(-dir.Z, dir.X);
            float pitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
            var beltRot = Matrix4x4.CreateRotationZ(pitch) * Matrix4x4.CreateRotationY(yaw);
            var flatRot = Matrix4x4.CreateRotationY(yaw);
            var mid = (c.TailPos + c.HeadPos) * 0.5f;
            float halfW = c.Width * 0.5f;

            // 1. Rubber belt bed
            var beltMat = Matrix4x4.CreateScale(len, 0.08f, c.Width * 0.90f) * beltRot * Matrix4x4.CreateTranslation(mid);
            AppendBox(v, idx, beltMat, 0.11f, 0.13f, 0.16f, 0.15f, 0.85f);

            // 2. Left & right steel side stringer channels
            foreach (float side in new[] { -halfW, halfW })
            {
                var sidePos = mid + Vector3.Transform(new Vector3(0f, -0.04f, side), beltRot);
                var strMat = Matrix4x4.CreateScale(len + 0.35f, 0.22f, 0.08f) * beltRot * Matrix4x4.CreateTranslation(sidePos);
                AppendBox(v, idx, strMat, 0.30f, 0.37f, 0.45f, 0.72f, 0.40f);
            }

            // 3. Cross-bracing struts under belt every ~1.8m
            int crossCount = Math.Max(2, (int)(len / 1.8f));
            for (int i = 0; i <= crossCount; i++)
            {
                var p = Vector3.Lerp(c.TailPos, c.HeadPos, (float)i / crossCount);
                var crossMat = Matrix4x4.CreateScale(0.08f, 0.10f, c.Width + 0.06f) * beltRot * Matrix4x4.CreateTranslation(p.X, p.Y - 0.12f, p.Z);
                AppendBox(v, idx, crossMat, 0.26f, 0.32f, 0.40f, 0.7f, 0.45f);
            }

            // 4. Vertical structural support bents / trestles extending down to Y=0 every ~3.5m
            int bentCount = Math.Max(2, (int)Math.Ceiling(len / 3.5f) + 1);
            for (int i = 0; i < bentCount; i++)
            {
                float frac = Math.Clamp(0.10f + 0.80f * i / Math.Max(1, bentCount - 1), 0.08f, 0.92f);
                var bp = Vector3.Lerp(c.TailPos, c.HeadPos, frac);
                float h = bp.Y - 0.12f;
                if (h <= 0.18f) continue;

                foreach (float side in new[] { -halfW - 0.06f, halfW + 0.06f })
                {
                    var legOff = Vector3.Transform(new Vector3(0f, 0f, side), flatRot);
                    var legMat = Matrix4x4.CreateScale(0.11f, h, 0.11f)
                               * flatRot
                               * Matrix4x4.CreateTranslation(bp.X + legOff.X, h * 0.5f, bp.Z + legOff.Z);
                    AppendBox(v, idx, legMat, 0.25f, 0.31f, 0.39f, 0.72f, 0.45f);
                }

                // Trestle cross-beam at mid-height
                if (h > 1.2f)
                {
                    var braceMat = Matrix4x4.CreateScale(0.08f, 0.08f, c.Width + 0.18f)
                                 * flatRot
                                 * Matrix4x4.CreateTranslation(bp.X, h * 0.52f, bp.Z);
                    AppendBox(v, idx, braceMat, 0.28f, 0.34f, 0.42f, 0.72f, 0.45f);
                }
            }

            // 5. Side maintenance walkway + safety-yellow handrail on elevated/inclined conveyors
            if (len > 4.2f || MathF.Abs(c.HeadPos.Y - c.TailPos.Y) > 0.6f)
            {
                float walkZ = halfW + 0.34f;
                var walkMid = mid + Vector3.Transform(new Vector3(0f, -0.05f, walkZ), beltRot);
                var walkMat = Matrix4x4.CreateScale(len * 0.92f, 0.05f, 0.52f) * beltRot * Matrix4x4.CreateTranslation(walkMid);
                AppendBox(v, idx, walkMat, 0.20f, 0.24f, 0.30f, 0.6f, 0.5f);

                // Safety yellow top handrail & vertical posts
                float railZ = halfW + 0.58f;
                var railMid = mid + Vector3.Transform(new Vector3(0f, 0.78f, railZ), beltRot);
                var railMat = Matrix4x4.CreateScale(len * 0.92f, 0.05f, 0.05f) * beltRot * Matrix4x4.CreateTranslation(railMid);
                AppendBox(v, idx, railMat, 0.95f, 0.76f, 0.08f, 0.35f, 0.45f);

                int posts = Math.Max(3, (int)(len / 2.0f));
                for (int p = 0; p <= posts; p++)
                {
                    float pf = 0.04f + 0.92f * p / posts;
                    var pp = Vector3.Lerp(c.TailPos, c.HeadPos, pf) + Vector3.Transform(new Vector3(0f, 0.38f, railZ), beltRot);
                    var postMat = Matrix4x4.CreateScale(0.045f, 0.80f, 0.045f) * flatRot * Matrix4x4.CreateTranslation(pp);
                    AppendBox(v, idx, postMat, 0.95f, 0.76f, 0.08f, 0.35f, 0.45f);
                }
            }

            // 6. Receiving Skirtboard Hopper at TailPos
            foreach (float side in new[] { -halfW + 0.05f, halfW - 0.05f })
            {
                var skPos = c.TailPos + Vector3.Transform(new Vector3(0.45f, 0.22f, side), beltRot);
                var skMat = Matrix4x4.CreateScale(1.1f, 0.36f, 0.07f) * beltRot * Matrix4x4.CreateTranslation(skPos);
                AppendBox(v, idx, skMat, 0.36f, 0.44f, 0.52f, 0.65f, 0.45f);
            }

            // 7. Head Drive Motor & Gearbox block beside HeadPos
            var motorPos = c.HeadPos + Vector3.Transform(new Vector3(-0.25f, -0.05f, -halfW - 0.32f), flatRot);
            var motorMat = Matrix4x4.CreateScale(0.65f, 0.42f, 0.44f) * flatRot * Matrix4x4.CreateTranslation(motorPos);
            AppendBox(v, idx, motorMat, 0.12f, 0.35f, 0.58f, 0.65f, 0.38f);

            // 8. Enclosed Transfer Chute from HeadPos down to HeadDropTargetY
            var chuteBot = c.GetChuteBottomPos();
            var chuteTop = c.HeadPos + new Vector3(0f, 0.10f, 0f);
            var chuteMid = (chuteTop + chuteBot) * 0.5f;
            float chuteH = MathF.Max(0.4f, Vector3.Distance(chuteTop, chuteBot));
            var chuteDir = Vector3.Normalize(chuteTop - chuteBot);
            var chuteRot = Matrix4x4.CreateRotationY(yaw);
            if (MathF.Abs(chuteDir.X) + MathF.Abs(chuteDir.Z) > 0.05f)
            {
                float cYaw = MathF.Atan2(-chuteDir.Z, chuteDir.X);
                float tilt = MathF.Acos(Math.Clamp(chuteDir.Y, -1f, 1f));
                chuteRot = Matrix4x4.CreateRotationZ(-tilt) * Matrix4x4.CreateRotationY(cYaw);
            }
            var chuteMat = Matrix4x4.CreateScale(0.68f, chuteH, c.Width * 0.86f) * chuteRot * Matrix4x4.CreateTranslation(chuteMid);
            AppendBox(v, idx, chuteMat, 0.24f, 0.30f, 0.38f, 0.68f, 0.48f);

            // 9. Optional curved feed chute (e.g., SS-101 top deck -> CV-106 tail)
            if (c.FeedChuteWaypoints is { Length: >= 2 })
            {
                for (int w = 0; w < c.FeedChuteWaypoints.Length - 1; w++)
                {
                    var a = c.FeedChuteWaypoints[w];
                    var b = c.FeedChuteWaypoints[w + 1];
                    var sMid = (a + b) * 0.5f;
                    var sDelta = b - a;
                    float sLen = sDelta.Length();
                    if (sLen < 0.05f) continue;
                    var sDir = sDelta / sLen;
                    float sYaw = MathF.Atan2(-sDir.Z, sDir.X);
                    float sPitch = MathF.Asin(Math.Clamp(sDir.Y, -1f, 1f));
                    var sMat = Matrix4x4.CreateScale(sLen + 0.15f, 0.38f, 0.62f)
                             * Matrix4x4.CreateRotationZ(sPitch)
                             * Matrix4x4.CreateRotationY(sYaw)
                             * Matrix4x4.CreateTranslation(sMid);
                    AppendBox(v, idx, sMat, 0.32f, 0.40f, 0.48f, 0.68f, 0.45f);
                }
            }
        }

        return UploadIndexedMesh(v.ToArray(), idx.ToArray());
    }
}
