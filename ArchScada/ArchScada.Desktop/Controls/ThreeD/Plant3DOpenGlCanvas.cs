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
/// Hardware-accelerated OpenGL 3.2+ Core / OpenGL ES 3.0 viewport for the CHPP 3D Digital Twin.
/// Implements a 3-pass HDR PBR pipeline:
///   Pass 1: 2048x2048 directional shadow map with polygon offset
///   Pass 2: 4x MSAA RGBA16F HDR offscreen multi-scatter GGX + wet-coal clearcoat PBR + sky IBL + 16-tap Vogel PCSS shadows + hardware instancing
///   Pass 3: MSAA resolve + 12-tap bilateral GTAO-Lite + highlight bloom + ACES Filmic tone mapping + FidelityFX CAS sharpening
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
    private const int GL_UNSIGNED_INT = 0x1405;
    private const int GL_UNSIGNED_INT_24_8 = 0x84FA;
    private const int GL_FLOAT = 0x1406;
    private const int GL_ARRAY_BUFFER = 0x8892;
    private const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    private const int GL_STATIC_DRAW = 0x88E4;
    private const int GL_DYNAMIC_DRAW = 0x88E8;
    private const int GL_VERTEX_SHADER = 0x8B31;
    private const int GL_FRAGMENT_SHADER = 0x8B30;
    private const int GL_COMPILE_STATUS = 0x8B81;
    private const int GL_LINK_STATUS = 0x8B82;
    private const int GL_RGBA = 0x1908;
    private const int GL_RGBA8 = 0x8058;
    private const int GL_RGBA16F = 0x881A;
    private const int GL_HALF_FLOAT = 0x140B;
    private const int GL_DEPTH_COMPONENT = 0x1902;
    private const int GL_DEPTH_COMPONENT24 = 0x81A6;
    private const int GL_DEPTH_STENCIL = 0x84F9;
    private const int GL_DEPTH24_STENCIL8 = 0x88F0;
    private const int GL_UNSIGNED_BYTE = 0x1401;
    private const int GL_PACK_ALIGNMENT = 0x0D05;
    private const int GL_MULTISAMPLE = 0x809D;
    private const int GL_POLYGON_OFFSET_FILL = 0x8037;
    private const int GL_TEXTURE_2D = 0x0DE1;
    private const int GL_TEXTURE0 = 0x84C0;
    private const int GL_TEXTURE1 = 0x84C1;
    private const int GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_TEXTURE_MIN_FILTER = 0x2801;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_NEAREST = 0x2600;
    private const int GL_LINEAR = 0x2601;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_FRAMEBUFFER = 0x8D40;
    private const int GL_READ_FRAMEBUFFER = 0x8CA8;
    private const int GL_DRAW_FRAMEBUFFER = 0x8CA9;
    private const int GL_RENDERBUFFER = 0x8D41;
    private const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const int GL_DEPTH_ATTACHMENT = 0x8D00;
    private const int GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    private const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    private const int GL_FRAMEBUFFER_BINDING = 0x8CA6;
    private const int GL_NONE = 0;

    private const int ShadowMapSize = 2048;
    private const int InstanceFloats = 22; // mat4(16) + vec4 colorAlpha(4) + vec2 pbr(2)

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlViewportDel(int x, int y, int w, int h);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlClearColorDel(float r, float g, float b, float a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlClearDel(int mask);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlEnableDel(int cap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDisableDel(int cap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBlendFuncDel(int sfactor, int dfactor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDepthMaskDel(byte flag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlCullFaceDel(int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlPolygonOffsetDel(float factor, float units);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlCreateShaderDel(int type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlShaderSourceDel(int shader, int count, byte** strings, int* length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlCompileShaderDel(int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetShaderivDel(int shader, int pname, int* param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetShaderInfoLogDel(int shader, int maxLength, int* length, byte* infoLog);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteShaderDel(int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlCreateProgramDel();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlAttachShaderDel(int program, int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindAttribLocationDel(int program, uint index, byte* name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlLinkProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetProgramivDel(int program, int pname, int* param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetProgramInfoLogDel(int program, int maxLength, int* length, byte* infoLog);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUseProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteProgramDel(int program);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlGetUniformLocationDel(int program, byte* name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniformMatrix4fvDel(int loc, int count, byte transpose, float* value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniformMatrix3fvDel(int loc, int count, byte transpose, float* value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform3fDel(int loc, float x, float y, float z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform2fDel(int loc, float x, float y);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform1fDel(int loc, float v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlUniform1iDel(int loc, int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenVertexArraysDel(int n, uint* arrays);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindVertexArrayDel(uint array);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteVertexArraysDel(int n, uint* arrays);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenBuffersDel(int n, uint* buffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindBufferDel(int target, uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBufferDataDel(int target, nint size, void* data, int usage);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBufferSubDataDel(int target, nint offset, nint size, void* data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteBuffersDel(int n, uint* buffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlEnableVertexAttribArrayDel(uint index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlVertexAttribPointerDel(uint index, int size, int type, byte normalized, int stride, nint pointer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlVertexAttribDivisorDel(uint index, uint divisor);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawElementsDel(int mode, int count, int type, nint indices);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawElementsInstancedDel(int mode, int count, int type, nint indices, int instancecount);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawArraysDel(int mode, int first, int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlReadPixelsDel(int x, int y, int width, int height, int format, int type, void* pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlPixelStoreiDel(int pname, int param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlFinishDel();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenFramebuffersDel(int n, uint* framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteFramebuffersDel(int n, uint* framebuffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindFramebufferDel(int target, uint framebuffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GlCheckFramebufferStatusDel(int target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlFramebufferTexture2DDel(int target, int attachment, int textarget, uint texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlFramebufferRenderbufferDel(int target, int attachment, int renderbuffertarget, uint renderbuffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBlitFramebufferDel(int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenRenderbuffersDel(int n, uint* renderbuffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteRenderbuffersDel(int n, uint* renderbuffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindRenderbufferDel(int target, uint renderbuffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlRenderbufferStorageMultisampleDel(int target, int samples, int internalformat, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGenTexturesDel(int n, uint* textures);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDeleteTexturesDel(int n, uint* textures);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlBindTextureDel(int target, uint texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlActiveTextureDel(int texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlTexImage2DDel(int target, int level, int internalformat, int width, int height, int border, int format, int type, void* pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlTexParameteriDel(int target, int pname, int param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawBuffersDel(int n, uint* bufs);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlDrawBufferDel(int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GlGetIntegervDel(int pname, int* data);

    private GlViewportDel? _glViewport;
    private GlClearColorDel? _glClearColor;
    private GlClearDel? _glClear;
    private GlEnableDel? _glEnable;
    private GlDisableDel? _glDisable;
    private GlBlendFuncDel? _glBlendFunc;
    private GlDepthMaskDel? _glDepthMask;
    private GlCullFaceDel? _glCullFace;
    private GlPolygonOffsetDel? _glPolygonOffset;
    private GlCreateShaderDel? _glCreateShader;
    private GlShaderSourceDel? _glShaderSource;
    private GlCompileShaderDel? _glCompileShader;
    private GlGetShaderivDel? _glGetShaderiv;
    private GlGetShaderInfoLogDel? _glGetShaderInfoLog;
    private GlDeleteShaderDel? _glDeleteShader;
    private GlCreateProgramDel? _glCreateProgram;
    private GlAttachShaderDel? _glAttachShader;
    private GlBindAttribLocationDel? _glBindAttribLocation;
    private GlLinkProgramDel? _glLinkProgram;
    private GlGetProgramivDel? _glGetProgramiv;
    private GlGetProgramInfoLogDel? _glGetProgramInfoLog;
    private GlUseProgramDel? _glUseProgram;
    private GlDeleteProgramDel? _glDeleteProgram;
    private GlGetUniformLocationDel? _glGetUniformLocation;
    private GlUniformMatrix4fvDel? _glUniformMatrix4fv;
    private GlUniformMatrix3fvDel? _glUniformMatrix3fv;
    private GlUniform3fDel? _glUniform3f;
    private GlUniform2fDel? _glUniform2f;
    private GlUniform1fDel? _glUniform1f;
    private GlUniform1iDel? _glUniform1i;
    private GlGenVertexArraysDel? _glGenVertexArrays;
    private GlBindVertexArrayDel? _glBindVertexArray;
    private GlDeleteVertexArraysDel? _glDeleteVertexArrays;
    private GlGenBuffersDel? _glGenBuffers;
    private GlBindBufferDel? _glBindBuffer;
    private GlBufferDataDel? _glBufferData;
    private GlBufferSubDataDel? _glBufferSubData;
    private GlDeleteBuffersDel? _glDeleteBuffers;
    private GlEnableVertexAttribArrayDel? _glEnableVertexAttribArray;
    private GlVertexAttribPointerDel? _glVertexAttribPointer;
    private GlVertexAttribDivisorDel? _glVertexAttribDivisor;
    private GlDrawElementsDel? _glDrawElements;
    private GlDrawElementsInstancedDel? _glDrawElementsInstanced;
    private GlDrawArraysDel? _glDrawArrays;
    private GlReadPixelsDel? _glReadPixels;
    private GlPixelStoreiDel? _glPixelStorei;
    private GlFinishDel? _glFinish;
    private GlGenFramebuffersDel? _glGenFramebuffers;
    private GlDeleteFramebuffersDel? _glDeleteFramebuffers;
    private GlBindFramebufferDel? _glBindFramebuffer;
    private GlCheckFramebufferStatusDel? _glCheckFramebufferStatus;
    private GlFramebufferTexture2DDel? _glFramebufferTexture2D;
    private GlFramebufferRenderbufferDel? _glFramebufferRenderbuffer;
    private GlBlitFramebufferDel? _glBlitFramebuffer;
    private GlGenRenderbuffersDel? _glGenRenderbuffers;
    private GlDeleteRenderbuffersDel? _glDeleteRenderbuffers;
    private GlBindRenderbufferDel? _glBindRenderbuffer;
    private GlRenderbufferStorageMultisampleDel? _glRenderbufferStorageMultisample;
    private GlGenTexturesDel? _glGenTextures;
    private GlDeleteTexturesDel? _glDeleteTextures;
    private GlBindTextureDel? _glBindTexture;
    private GlActiveTextureDel? _glActiveTexture;
    private GlTexImage2DDel? _glTexImage2D;
    private GlTexParameteriDel? _glTexParameteri;
    private GlDrawBuffersDel? _glDrawBuffers;
    private GlDrawBufferDel? _glDrawBuffer;
    private GlGetIntegervDel? _glGetIntegerv;

    private readonly object _captureLock = new();
    private byte[]? _lastFrameRgba;
    private int _lastFrameWidth;
    private int _lastFrameHeight;

    private sealed class GpuMesh
    {
        public uint Vao;
        public uint InstancedVao;
        public uint Vbo;
        public uint Ebo;
        public int IndexCount;
    }

    private readonly record struct InstanceItem(Matrix4x4 Model, float R, float G, float B, float Alpha, float Metallic, float Roughness);

    private readonly Dictionary<string, GpuMesh> _glbGpuMeshes = new(StringComparer.OrdinalIgnoreCase);
    private GpuMesh? _unitBox;
    private GpuMesh? _unitCylinder;
    private GpuMesh? _unitRing;
    private GpuMesh? _unitCone;
    private GpuMesh? _groundTerrainQuad;
    private GpuMesh? _pipesMesh;
    private GpuMesh? _conveyorStructureMesh;
    private GpuMesh? _coalLumpMesh;

    private uint _instanceVbo;
    private int _instanceCapacity = 2048;
    private float[] _instanceScratch = new float[2048 * InstanceFloats];

    private readonly List<InstanceItem> _groundPadBoxes = new(16);
    private readonly List<InstanceItem> _casterBoxes = new(512);
    private readonly List<InstanceItem> _casterCylinders = new(64);
    private readonly List<InstanceItem> _coalLumps = new(1024);
    private readonly List<InstanceItem> _unlitBoxes = new(96);
    private readonly List<InstanceItem> _unlitCones = new(64);
    private readonly List<InstanceItem> _fillCylinders = new(8);
    private readonly List<InstanceItem> _softHighlightBoxes = new(32);
    private readonly List<InstanceItem> _softParticleBoxes = new(96);
    private readonly List<InstanceItem> _ringInstances = new(32);

    // Pass 1: Shadow Map
    private int _shadowProgram;
    private int _uShadowLightVp = -1;
    private int _uShadowModel = -1;
    private int _uShadowIsInstanced = -1;
    private uint _shadowFbo;
    private uint _shadowDepthTex;
    private bool _shadowReady;

    // Pass 2: Main PBR + MSAA
    private int _program;
    private int _uModel = -1;
    private int _uNormalMat = -1;
    private int _uViewProj = -1;
    private int _uLightViewProj = -1;
    private int _uCamPos = -1;
    private int _uTint = -1;
    private int _uTintMix = -1;
    private int _uAlpha = -1;
    private int _uUnlit = -1;
    private int _uIsInstanced = -1;
    private int _uShadowMap = -1;
    private int _uShadowsEnabled = -1;
    private int _uEncodeHdr = -1;
    private int _uApplyToneMap = -1;
    private int _uEnableClearcoat = -1;
    private int _uSimTime = -1;

    private uint _msaaFbo;
    private uint _msaaColorRbo;
    private uint _msaaDepthRbo;
    private bool _msaaReady;

    // Pass 3: Post-FX (Bilateral GTAO + Bloom + ACES + CAS Sharpening + Gamma)
    private int _postProgram;
    private int _uPostColorTex = -1;
    private int _uPostDepthTex = -1;
    private int _uPostTexelSize = -1;
    private int _uPostEnableAo = -1;
    private int _uPostEnableCas = -1;
    private int _uPostDecodeHdr = -1;
    private int _uPostIsOrtho = -1;
    private int _uPostNear = -1;
    private int _uPostFar = -1;
    private int _uPostInvViewProj = -1;
    private uint _postVao;
    private uint _postFbo;
    private uint _postColorTex;
    private uint _postDepthTex;
    private bool _postReady;
    private bool _useFloatHdrFbo;

    private int _offscreenW;
    private int _offscreenH;
    private int _frameDrawCalls;

    public double CameraYaw { get; set; } = -0.58;
    public double CameraPitch { get; set; } = 0.56;
    public double CameraZoom { get; set; } = 0.68;
    public double TargetX { get; set; } = -2.0;
    public double TargetY { get; set; } = 0.9;
    public double TargetZ { get; set; } = -3.0;
    public double SimTime { get; set; }
    public IReadOnlyList<EquipmentItem>? Equipment { get; set; }
    public Unity3DEffectSettings? Effects { get; set; }
    public string? SelectedEquipmentId { get; set; }
    public string? SelectedNodeId { get; set; }
    public string? HoveredNodeId { get; set; }

    public bool IsHardwareActive { get; private set; }
    public int LoadedGlbCount => _glbGpuMeshes.Count;
    public int LastFrameDrawCalls { get; private set; }
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
            _glCullFace = LoadProc<GlCullFaceDel>(gl, "glCullFace");
            _glPolygonOffset = LoadProc<GlPolygonOffsetDel>(gl, "glPolygonOffset");
            _glCreateShader = LoadProc<GlCreateShaderDel>(gl, "glCreateShader");
            _glShaderSource = LoadProc<GlShaderSourceDel>(gl, "glShaderSource");
            _glCompileShader = LoadProc<GlCompileShaderDel>(gl, "glCompileShader");
            _glGetShaderiv = LoadProc<GlGetShaderivDel>(gl, "glGetShaderiv");
            _glGetShaderInfoLog = LoadProc<GlGetShaderInfoLogDel>(gl, "glGetShaderInfoLog");
            _glDeleteShader = LoadProc<GlDeleteShaderDel>(gl, "glDeleteShader");
            _glCreateProgram = LoadProc<GlCreateProgramDel>(gl, "glCreateProgram");
            _glAttachShader = LoadProc<GlAttachShaderDel>(gl, "glAttachShader");
            _glBindAttribLocation = LoadProc<GlBindAttribLocationDel>(gl, "glBindAttribLocation");
            _glLinkProgram = LoadProc<GlLinkProgramDel>(gl, "glLinkProgram");
            _glGetProgramiv = LoadProc<GlGetProgramivDel>(gl, "glGetProgramiv");
            _glGetProgramInfoLog = LoadProc<GlGetProgramInfoLogDel>(gl, "glGetProgramInfoLog");
            _glUseProgram = LoadProc<GlUseProgramDel>(gl, "glUseProgram");
            _glDeleteProgram = LoadProc<GlDeleteProgramDel>(gl, "glDeleteProgram");
            _glGetUniformLocation = LoadProc<GlGetUniformLocationDel>(gl, "glGetUniformLocation");
            _glUniformMatrix4fv = LoadProc<GlUniformMatrix4fvDel>(gl, "glUniformMatrix4fv");
            _glUniformMatrix3fv = LoadProc<GlUniformMatrix3fvDel>(gl, "glUniformMatrix3fv");
            _glUniform3f = LoadProc<GlUniform3fDel>(gl, "glUniform3f");
            _glUniform2f = LoadProc<GlUniform2fDel>(gl, "glUniform2f");
            _glUniform1f = LoadProc<GlUniform1fDel>(gl, "glUniform1f");
            _glUniform1i = LoadProc<GlUniform1iDel>(gl, "glUniform1i");
            _glGenVertexArrays = LoadProc<GlGenVertexArraysDel>(gl, "glGenVertexArrays");
            _glBindVertexArray = LoadProc<GlBindVertexArrayDel>(gl, "glBindVertexArray");
            _glDeleteVertexArrays = LoadProc<GlDeleteVertexArraysDel>(gl, "glDeleteVertexArrays");
            _glGenBuffers = LoadProc<GlGenBuffersDel>(gl, "glGenBuffers");
            _glBindBuffer = LoadProc<GlBindBufferDel>(gl, "glBindBuffer");
            _glBufferData = LoadProc<GlBufferDataDel>(gl, "glBufferData");
            _glBufferSubData = LoadProc<GlBufferSubDataDel>(gl, "glBufferSubData");
            _glDeleteBuffers = LoadProc<GlDeleteBuffersDel>(gl, "glDeleteBuffers");
            _glEnableVertexAttribArray = LoadProc<GlEnableVertexAttribArrayDel>(gl, "glEnableVertexAttribArray");
            _glVertexAttribPointer = LoadProc<GlVertexAttribPointerDel>(gl, "glVertexAttribPointer");
            _glVertexAttribDivisor = LoadProc<GlVertexAttribDivisorDel>(gl, "glVertexAttribDivisor");
            _glDrawElements = LoadProc<GlDrawElementsDel>(gl, "glDrawElements");
            _glDrawElementsInstanced = LoadProc<GlDrawElementsInstancedDel>(gl, "glDrawElementsInstanced");
            _glDrawArrays = LoadProc<GlDrawArraysDel>(gl, "glDrawArrays");
            _glReadPixels = LoadProc<GlReadPixelsDel>(gl, "glReadPixels");
            _glPixelStorei = LoadProc<GlPixelStoreiDel>(gl, "glPixelStorei");
            _glFinish = LoadProc<GlFinishDel>(gl, "glFinish");
            _glGenFramebuffers = LoadProc<GlGenFramebuffersDel>(gl, "glGenFramebuffers");
            _glDeleteFramebuffers = LoadProc<GlDeleteFramebuffersDel>(gl, "glDeleteFramebuffers");
            _glBindFramebuffer = LoadProc<GlBindFramebufferDel>(gl, "glBindFramebuffer");
            _glCheckFramebufferStatus = LoadProc<GlCheckFramebufferStatusDel>(gl, "glCheckFramebufferStatus");
            _glFramebufferTexture2D = LoadProc<GlFramebufferTexture2DDel>(gl, "glFramebufferTexture2D");
            _glFramebufferRenderbuffer = LoadProc<GlFramebufferRenderbufferDel>(gl, "glFramebufferRenderbuffer");
            _glBlitFramebuffer = LoadProc<GlBlitFramebufferDel>(gl, "glBlitFramebuffer");
            _glGenRenderbuffers = LoadProc<GlGenRenderbuffersDel>(gl, "glGenRenderbuffers");
            _glDeleteRenderbuffers = LoadProc<GlDeleteRenderbuffersDel>(gl, "glDeleteRenderbuffers");
            _glBindRenderbuffer = LoadProc<GlBindRenderbufferDel>(gl, "glBindRenderbuffer");
            _glRenderbufferStorageMultisample = LoadProc<GlRenderbufferStorageMultisampleDel>(gl, "glRenderbufferStorageMultisample");
            _glGenTextures = LoadProc<GlGenTexturesDel>(gl, "glGenTextures");
            _glDeleteTextures = LoadProc<GlDeleteTexturesDel>(gl, "glDeleteTextures");
            _glBindTexture = LoadProc<GlBindTextureDel>(gl, "glBindTexture");
            _glActiveTexture = LoadProc<GlActiveTextureDel>(gl, "glActiveTexture");
            _glTexImage2D = LoadProc<GlTexImage2DDel>(gl, "glTexImage2D");
            _glTexParameteri = LoadProc<GlTexParameteriDel>(gl, "glTexParameteri");
            _glDrawBuffers = LoadProc<GlDrawBuffersDel>(gl, "glDrawBuffers");
            _glDrawBuffer = LoadProc<GlDrawBufferDel>(gl, "glDrawBuffer");
            _glGetIntegerv = LoadProc<GlGetIntegervDel>(gl, "glGetIntegerv");

            if (_glCreateProgram is null || _glGenVertexArrays is null || _glDrawElements is null)
                return;

            bool isEs = GlVersion.Type == GlProfileType.OpenGLES;

            _program = BuildMainPbrProgram(isEs);
            if (_program == 0)
                return;

            _uModel = GetUniformLoc(_program, "uModel");
            _uNormalMat = GetUniformLoc(_program, "uNormalMat");
            _uViewProj = GetUniformLoc(_program, "uViewProj");
            _uLightViewProj = GetUniformLoc(_program, "uLightViewProj");
            _uCamPos = GetUniformLoc(_program, "uCamPos");
            _uTint = GetUniformLoc(_program, "uTint");
            _uTintMix = GetUniformLoc(_program, "uTintMix");
            _uAlpha = GetUniformLoc(_program, "uAlpha");
            _uUnlit = GetUniformLoc(_program, "uUnlit");
            _uIsInstanced = GetUniformLoc(_program, "uIsInstanced");
            _uShadowMap = GetUniformLoc(_program, "uShadowMap");
            _uShadowsEnabled = GetUniformLoc(_program, "uShadowsEnabled");
            _uEncodeHdr = GetUniformLoc(_program, "uEncodeHdr");
            _uApplyToneMap = GetUniformLoc(_program, "uApplyToneMap");
            _uEnableClearcoat = GetUniformLoc(_program, "uEnableClearcoat");
            _uSimTime = GetUniformLoc(_program, "uSimTime");

            _shadowProgram = BuildShadowProgram(isEs);
            if (_shadowProgram != 0)
            {
                _uShadowLightVp = GetUniformLoc(_shadowProgram, "uLightViewProj");
                _uShadowModel = GetUniformLoc(_shadowProgram, "uModel");
                _uShadowIsInstanced = GetUniformLoc(_shadowProgram, "uIsInstanced");
            }

            _postProgram = BuildPostProgram(isEs);
            if (_postProgram != 0)
            {
                _uPostColorTex = GetUniformLoc(_postProgram, "uColorTex");
                _uPostDepthTex = GetUniformLoc(_postProgram, "uDepthTex");
                _uPostTexelSize = GetUniformLoc(_postProgram, "uTexelSize");
                _uPostEnableAo = GetUniformLoc(_postProgram, "uEnableAo");
                _uPostEnableCas = GetUniformLoc(_postProgram, "uEnableCas");
                _uPostDecodeHdr = GetUniformLoc(_postProgram, "uDecodeHdr");
                _uPostIsOrtho = GetUniformLoc(_postProgram, "uIsOrtho");
                _uPostNear = GetUniformLoc(_postProgram, "uNear");
                _uPostFar = GetUniformLoc(_postProgram, "uFar");
                _uPostInvViewProj = GetUniformLoc(_postProgram, "uInvViewProj");
                uint postVao = 0;
                _glGenVertexArrays!(1, &postVao);
                _postVao = postVao;
            }

            // Instance VBO shared across instanced VAOs
            uint instVbo = 0;
            _glGenBuffers!(1, &instVbo);
            _instanceVbo = instVbo;
            _glBindBuffer!(GL_ARRAY_BUFFER, _instanceVbo);
            _glBufferData!(GL_ARRAY_BUFFER, (nint)(_instanceCapacity * InstanceFloats * sizeof(float)), null, GL_DYNAMIC_DRAW);
            _glBindBuffer!(GL_ARRAY_BUFFER, 0);

            foreach (var (fileName, meshData) in Plant3DSceneData.LoadAllGlbMeshes())
            {
                _glbGpuMeshes[fileName] = UploadGlbMesh(fileName, meshData);
            }

            _unitBox = BuildUnitBoxMesh();
            _unitCylinder = BuildUnitCylinderMesh(20);
            _unitRing = BuildUnitRingMesh(32, 0.88f, 1.0f);
            _unitCone = BuildUnitConeMesh(14);
            _groundTerrainQuad = BuildGroundTerrainQuad();
            _pipesMesh = BuildOrthogonalPipesMesh();
            _conveyorStructureMesh = BuildConveyorStructuresMesh();
            _coalLumpMesh = BuildCoalLumpMesh();

            InitShadowFbo();

            IsHardwareActive = true;
        }
        catch (Exception ex)
        {
            // Log the failure so headless/no-GPU or bad-mesh init is diagnosable instead of silent.
            Console.Error.WriteLine($"[Plant3D] OpenGL init failed: {ex}");
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
        DeleteMesh(_groundTerrainQuad); _groundTerrainQuad = null;
        DeleteMesh(_pipesMesh); _pipesMesh = null;
        DeleteMesh(_conveyorStructureMesh); _conveyorStructureMesh = null;
        DeleteMesh(_coalLumpMesh); _coalLumpMesh = null;

        if (_instanceVbo != 0)
        {
            uint iv = _instanceVbo;
            _glDeleteBuffers?.Invoke(1, &iv);
            _instanceVbo = 0;
        }

        if (_postVao != 0)
        {
            uint pv = _postVao;
            _glDeleteVertexArrays?.Invoke(1, &pv);
            _postVao = 0;
        }

        DeleteShadowFbo();
        DeleteOffscreenFbos();

        if (_program != 0) { _glDeleteProgram?.Invoke(_program); _program = 0; }
        if (_shadowProgram != 0) { _glDeleteProgram?.Invoke(_shadowProgram); _shadowProgram = 0; }
        if (_postProgram != 0) { _glDeleteProgram?.Invoke(_postProgram); _postProgram = 0; }

        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlLost()
    {
        // Context is already lost: reset all managed GL handles/state without invoking GL delete procs.
        IsHardwareActive = false;
        _glbGpuMeshes.Clear();
        _unitBox = _unitCylinder = _unitRing = _unitCone = _groundTerrainQuad = _pipesMesh = _conveyorStructureMesh = _coalLumpMesh = null;
        _instanceVbo = _postVao = _shadowFbo = _shadowDepthTex = _msaaFbo = _msaaColorRbo = _msaaDepthRbo = _postFbo = _postColorTex = _postDepthTex = 0;
        _program = _shadowProgram = _postProgram = 0;
        _offscreenW = _offscreenH = 0;
        _shadowReady = _msaaReady = _postReady = false;
        base.OnOpenGlLost();
    }

    private bool HasFboSupport() =>
        _glGenFramebuffers is not null &&
        _glDeleteFramebuffers is not null &&
        _glBindFramebuffer is not null &&
        _glCheckFramebufferStatus is not null &&
        _glFramebufferTexture2D is not null &&
        _glFramebufferRenderbuffer is not null &&
        _glBlitFramebuffer is not null &&
        _glGenRenderbuffers is not null &&
        _glDeleteRenderbuffers is not null &&
        _glBindRenderbuffer is not null &&
        _glRenderbufferStorageMultisample is not null &&
        _glGenTextures is not null &&
        _glDeleteTextures is not null &&
        _glBindTexture is not null &&
        _glActiveTexture is not null &&
        _glTexImage2D is not null &&
        _glTexParameteri is not null;

    private void InitShadowFbo()
    {
        DeleteShadowFbo();
        if (!HasFboSupport() || _shadowProgram == 0)
            return;

        int prevFbo = 0;
        if (_glGetIntegerv is not null)
        {
            _glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prevFbo);
        }

        uint tex = 0, fbo = 0;
        _glGenTextures!(1, &tex);
        _glBindTexture!(GL_TEXTURE_2D, tex);
        _glTexImage2D!(GL_TEXTURE_2D, 0, GL_DEPTH_COMPONENT24, ShadowMapSize, ShadowMapSize, 0, GL_DEPTH_COMPONENT, GL_UNSIGNED_INT, null);
        _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        _glBindTexture!(GL_TEXTURE_2D, 0);

        int status;
        try
        {
            _glGenFramebuffers!(1, &fbo);
            _glBindFramebuffer!(GL_FRAMEBUFFER, fbo);
            _glFramebufferTexture2D!(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, tex, 0);

            if (_glDrawBuffers is not null)
            {
                uint none = GL_NONE;
                _glDrawBuffers(1, &none);
            }
            else
            {
                _glDrawBuffer?.Invoke(GL_NONE);
            }

            status = _glCheckFramebufferStatus!(GL_FRAMEBUFFER);
        }
        finally
        {
            _glBindFramebuffer!(GL_FRAMEBUFFER, (uint)Math.Max(0, prevFbo));
        }

        _shadowDepthTex = tex;
        _shadowFbo = fbo;
        _shadowReady = status == GL_FRAMEBUFFER_COMPLETE;
        if (!_shadowReady)
        {
            System.Diagnostics.Debug.WriteLine($"[Plant3D] Shadow FBO incomplete (status=0x{status:X}); disabling shadow map.");
            DeleteShadowFbo();
        }
    }

    private void DeleteShadowFbo()
    {
        _shadowReady = false;
        if (_shadowFbo != 0)
        {
            uint f = _shadowFbo;
            _glDeleteFramebuffers?.Invoke(1, &f);
            _shadowFbo = 0;
        }
        if (_shadowDepthTex != 0)
        {
            uint t = _shadowDepthTex;
            _glDeleteTextures?.Invoke(1, &t);
            _shadowDepthTex = 0;
        }
    }

    private void EnsureOffscreenFbos(int pw, int ph, uint returnFb)
    {
        pw = Math.Clamp(pw, 1, 8192);
        ph = Math.Clamp(ph, 1, 8192);
        if (_offscreenW == pw && _offscreenH == ph)
            return;

        DeleteOffscreenFbos();
        _offscreenW = pw;
        _offscreenH = ph;
        _useFloatHdrFbo = false;

        if (!HasFboSupport())
            return;

        if (_postProgram != 0 && _postVao != 0 && TryCreateOffscreenFbos(pw, ph, useFloatHdr: true))
        {
            _useFloatHdrFbo = true;
        }
        else
        {
            DeleteOffscreenFbos();
            _offscreenW = pw;
            _offscreenH = ph;
            TryCreateOffscreenFbos(pw, ph, useFloatHdr: false);
        }

        _glBindFramebuffer!(GL_FRAMEBUFFER, returnFb);
    }

    private bool TryCreateOffscreenFbos(int pw, int ph, bool useFloatHdr)
    {
        int colorInternalFmt = useFloatHdr ? GL_RGBA16F : GL_RGBA8;
        int colorPixelType = useFloatHdr ? GL_HALF_FLOAT : GL_UNSIGNED_BYTE;

        // 1. 4x MSAA FBO
        uint msaaFbo = 0, msaaColor = 0, msaaDepth = 0;
        _glGenFramebuffers!(1, &msaaFbo);
        _glGenRenderbuffers!(1, &msaaColor);
        _glGenRenderbuffers!(1, &msaaDepth);

        _glBindRenderbuffer!(GL_RENDERBUFFER, msaaColor);
        _glRenderbufferStorageMultisample!(GL_RENDERBUFFER, 4, colorInternalFmt, pw, ph);
        _glBindRenderbuffer!(GL_RENDERBUFFER, msaaDepth);
        _glRenderbufferStorageMultisample!(GL_RENDERBUFFER, 4, GL_DEPTH24_STENCIL8, pw, ph);
        _glBindRenderbuffer!(GL_RENDERBUFFER, 0);

        _glBindFramebuffer!(GL_FRAMEBUFFER, msaaFbo);
        _glFramebufferRenderbuffer!(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_RENDERBUFFER, msaaColor);
        _glFramebufferRenderbuffer!(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, msaaDepth);
        int msaaStatus = _glCheckFramebufferStatus!(GL_FRAMEBUFFER);

        _msaaFbo = msaaFbo;
        _msaaColorRbo = msaaColor;
        _msaaDepthRbo = msaaDepth;
        _msaaReady = msaaStatus == GL_FRAMEBUFFER_COMPLETE;

        if (!_msaaReady)
        {
            DeleteMsaaFboOnly();
            return false;
        }

        // 2. Single-sample post-processing FBO (matching internal format for glBlitFramebuffer)
        if (_postProgram != 0 && _postVao != 0)
        {
            uint postFbo = 0, postColor = 0, postDepth = 0;
            _glGenTextures!(1, &postColor);
            _glBindTexture!(GL_TEXTURE_2D, postColor);
            _glTexImage2D!(GL_TEXTURE_2D, 0, colorInternalFmt, pw, ph, 0, GL_RGBA, colorPixelType, null);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

            _glGenTextures!(1, &postDepth);
            _glBindTexture!(GL_TEXTURE_2D, postDepth);
            _glTexImage2D!(GL_TEXTURE_2D, 0, GL_DEPTH24_STENCIL8, pw, ph, 0, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, null);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            _glTexParameteri!(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            _glBindTexture!(GL_TEXTURE_2D, 0);

            _glGenFramebuffers!(1, &postFbo);
            _glBindFramebuffer!(GL_FRAMEBUFFER, postFbo);
            _glFramebufferTexture2D!(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, postColor, 0);
            _glFramebufferTexture2D!(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, postDepth, 0);
            int postStatus = _glCheckFramebufferStatus!(GL_FRAMEBUFFER);

            _postFbo = postFbo;
            _postColorTex = postColor;
            _postDepthTex = postDepth;
            _postReady = postStatus == GL_FRAMEBUFFER_COMPLETE;

            if (!_postReady)
            {
                DeletePostFboOnly();
                return false;
            }
        }

        return true;
    }

    private void DeleteMsaaFboOnly()
    {
        _msaaReady = false;
        if (_msaaFbo != 0) { uint f = _msaaFbo; _glDeleteFramebuffers?.Invoke(1, &f); _msaaFbo = 0; }
        if (_msaaColorRbo != 0) { uint r = _msaaColorRbo; _glDeleteRenderbuffers?.Invoke(1, &r); _msaaColorRbo = 0; }
        if (_msaaDepthRbo != 0) { uint r = _msaaDepthRbo; _glDeleteRenderbuffers?.Invoke(1, &r); _msaaDepthRbo = 0; }
    }

    private void DeletePostFboOnly()
    {
        _postReady = false;
        if (_postFbo != 0) { uint f = _postFbo; _glDeleteFramebuffers?.Invoke(1, &f); _postFbo = 0; }
        if (_postColorTex != 0) { uint t = _postColorTex; _glDeleteTextures?.Invoke(1, &t); _postColorTex = 0; }
        if (_postDepthTex != 0) { uint t = _postDepthTex; _glDeleteTextures?.Invoke(1, &t); _postDepthTex = 0; }
    }

    private void DeleteOffscreenFbos()
    {
        DeleteMsaaFboOnly();
        DeletePostFboOnly();
        _offscreenW = 0;
        _offscreenH = 0;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!IsHardwareActive || _program == 0)
            return;

        // Guard against a partially-initialized context where some GL delegates are null.
        if (_glViewport is null || _glClearColor is null || _glClear is null ||
            _glEnable is null || _glDisable is null || _glBlendFunc is null ||
            _glDepthMask is null || _glUseProgram is null || _glGetUniformLocation is null ||
            _glUniformMatrix4fv is null || _glGenVertexArrays is null ||
            _glBindVertexArray is null || _glDrawElements is null || _glDrawArrays is null)
            return;

        double scale = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        if (!double.IsFinite(scale) || scale <= 0.0)
            scale = 1.0;
        double bw = double.IsFinite(Bounds.Width) ? Bounds.Width : 1.0;
        double bh = double.IsFinite(Bounds.Height) ? Bounds.Height : 1.0;
        int pw = Math.Clamp((int)(bw * scale), 1, 8192);
        int ph = Math.Clamp((int)(bh * scale), 1, 8192);

        EnsureOffscreenFbos(pw, ph, (uint)fb);
        _frameDrawCalls = 0;

        var fx = Effects ?? new Unity3DEffectSettings();
        var (view, proj, camPos) = Plant3DSceneData.BuildCameraMatrices(
            CameraYaw, CameraPitch, CameraZoom, TargetX, TargetZ,
            Math.Max(1.0, bw), Math.Max(1.0, bh),
            fx.IsOrthographic, TargetY);
        var viewProj = view * proj;

        var lightDir = Vector3.Normalize(new Vector3(0.48f, 0.82f, 0.34f));
        var shadowCenter = new Vector3(-2.0f, 1.0f, -0.5f);
        var lightViewProj = Matrix4x4.CreateLookAt(shadowCenter + lightDir * 32f, shadowCenter, Vector3.UnitY)
                          * Plant3DSceneData.CreateOpenGlOrthographic(48f, 48f, 2f, 68f);

        var eqMap = new Dictionary<string, EquipmentItem>(StringComparer.OrdinalIgnoreCase);
        if (Equipment is not null)
        {
            foreach (var e in Equipment) eqMap[e.Id] = e;
        }

        var flowMap = Plant3DSceneData.EvaluateProcessFlows(eqMap, SimTime);
        eqMap.TryGetValue("AF-001", out var afEq);
        bool loaderActive = fx.AnimateConveyors && (afEq is null || afEq.Status is not (EquipmentStatus.Stopped or EquipmentStatus.Fault or EquipmentStatus.Offline));
        var loaderPose = Plant3DSceneData.ComputeLoaderPose(SimTime, loaderActive);

        string? activeSelected = SelectedNodeId ?? SelectedEquipmentId;
        BuildInstancedSceneBatches(eqMap, flowMap, loaderPose, fx, activeSelected, HoveredNodeId);

        // =====================================================================
        // PASS 1: 2048x2048 Directional Shadow Map
        // =====================================================================
        bool useShadows = fx.ShowGroundShadows && _shadowReady && _shadowProgram != 0;
        if (useShadows)
        {
            _glBindFramebuffer!(GL_FRAMEBUFFER, _shadowFbo);
            _glViewport!(0, 0, ShadowMapSize, ShadowMapSize);
            _glDepthMask!(1);
            _glEnable!(GL_DEPTH_TEST);
            _glDisable!(GL_BLEND);
            _glDisable!(GL_CULL_FACE);
            _glEnable!(GL_POLYGON_OFFSET_FILL);
            _glPolygonOffset?.Invoke(2.2f, 4.0f);
            _glClear!(GL_DEPTH_BUFFER_BIT);

            _glUseProgram!(_shadowProgram);
            SetMatrix(_uShadowLightVp, lightViewProj);
            _glUniform1f!(_uShadowIsInstanced, 0f);

            if (_conveyorStructureMesh is not null)
            {
                SetMatrix(_uShadowModel, Matrix4x4.Identity);
                DrawMesh(_conveyorStructureMesh);
            }

            if (_pipesMesh is not null)
            {
                SetMatrix(_uShadowModel, Matrix4x4.Identity);
                DrawMesh(_pipesMesh);
            }

            DrawMachineNodesShadowPass(loaderPose, eqMap, flowMap, fx);

            if (_unitBox is not null && _casterBoxes.Count > 0)
            {
                DrawInstancedBatch(_unitBox, _casterBoxes, isShadowPass: true);
            }
            if (_unitCylinder is not null && _casterCylinders.Count > 0)
            {
                DrawInstancedBatch(_unitCylinder, _casterCylinders, isShadowPass: true);
            }
            if (_coalLumpMesh is not null && _coalLumps.Count > 0)
            {
                DrawInstancedBatch(_coalLumpMesh, _coalLumps, isShadowPass: true);
            }

            _glDisable!(GL_POLYGON_OFFSET_FILL);
        }

        // =====================================================================
        // PASS 2: 4x MSAA Offscreen Cook-Torrance GGX PBR Pass
        // =====================================================================
        bool runPostPass = _msaaReady && _postReady && _postProgram != 0 && _postVao != 0;
        if (_msaaReady)
            _glBindFramebuffer!(GL_FRAMEBUFFER, _msaaFbo);
        else
            _glBindFramebuffer!(GL_FRAMEBUFFER, (uint)fb);

        _glViewport!(0, 0, pw, ph);
        bool encodeSqrtHdr = runPostPass && !_useFloatHdrFbo;
        if (runPostPass)
        {
            if (encodeSqrtHdr)
                _glClearColor!(0.0346f, 0.0490f, 0.0775f, 1.0f); // sqrt(linear(0.003,0.006,0.015) * 0.4)
            else
                _glClearColor!(0.003f, 0.006f, 0.015f, 1.0f); // linear HDR for RGBA16F
        }
        else
            _glClearColor!(0.031f, 0.055f, 0.106f, 1.0f);

        _glClear!(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        _glEnable!(GL_DEPTH_TEST);
        _glEnable!(GL_MULTISAMPLE);
        _glDisable!(GL_CULL_FACE);
        _glEnable!(GL_BLEND);
        _glBlendFunc!(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        _glUseProgram!(_program);

        if (_glActiveTexture is not null && _glBindTexture is not null && _shadowDepthTex != 0)
        {
            _glActiveTexture(GL_TEXTURE0);
            _glBindTexture(GL_TEXTURE_2D, _shadowDepthTex);
            _glUniform1i?.Invoke(_uShadowMap, 0);
        }

        SetMatrix(_uViewProj, viewProj);
        SetMatrix(_uLightViewProj, lightViewProj);
        _glUniform3f!(_uCamPos, camPos.X, camPos.Y, camPos.Z);
        _glUniform1f!(_uShadowsEnabled, useShadows ? 1f : 0f);
        _glUniform1f!(_uEncodeHdr, encodeSqrtHdr ? 1f : 0f);
        _glUniform1f!(_uApplyToneMap, runPostPass ? 0f : 1f);
        _glUniform1f?.Invoke(_uEnableClearcoat, fx.EnableWetCoalClearcoat ? 1f : 0f);
        _glUniform1f?.Invoke(_uSimTime, (float)SimTime);
        _glUniform1f!(_uIsInstanced, 0f);

        // 1. Yard Terrain Quad + Anti-Aliased fwidth() Grid (Pass 2 only)
        if (_groundTerrainQuad is not null)
        {
            SetModelAndNormalMatrix(Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.14f, 0.17f, 0.22f);
            _glUniform1f!(_uTintMix, 1.0f);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);
            DrawMesh(_groundTerrainQuad);
        }

        // 2. Concrete Foundation Pads + Circuit Accent Trim (Instanced)
        if (_unitBox is not null && _groundPadBoxes.Count > 0)
        {
            _glUniform1f!(_uUnlit, 0.0f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawInstancedBatch(_unitBox, _groundPadBoxes, isShadowPass: false);
        }

        // 3. Orthogonal 3D Slurry / Magnetite / Water Piping
        if (_pipesMesh is not null)
        {
            SetModelAndNormalMatrix(Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.55f, 0.64f, 0.75f);
            _glUniform1f!(_uTintMix, 0.20f);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);
            DrawMesh(_pipesMesh);
        }

        // 4. Procedural Industrial Conveyor Structures (trestles, stringers, walkways, handrails, transfer chutes)
        if (_conveyorStructureMesh is not null)
        {
            SetModelAndNormalMatrix(Matrix4x4.Identity);
            _glUniform3f!(_uTint, 0.45f, 0.54f, 0.64f);
            _glUniform1f!(_uTintMix, 0.04f);
            _glUniform1f!(_uAlpha, 1.0f);
            _glUniform1f!(_uUnlit, 0.0f);
            DrawMesh(_conveyorStructureMesh);
        }

        // 5. All 18 Machine Nodes (Blender .glb meshes)
        DrawMachineNodesMainPass(eqMap, flowMap, loaderPose, fx, activeSelected, HoveredNodeId);

        // 6. Instanced Opaque Mechanical Sub-Parts, Pulleys, Cleats, Faceted Coal Stream & Dump Boulders
        _glUniform1f!(_uUnlit, 0.0f);
        _glUniform1f!(_uTintMix, 1.0f);
        if (_unitBox is not null && _casterBoxes.Count > 0)
        {
            DrawInstancedBatch(_unitBox, _casterBoxes, isShadowPass: false);
        }
        if (_unitCylinder is not null && _casterCylinders.Count > 0)
        {
            DrawInstancedBatch(_unitCylinder, _casterCylinders, isShadowPass: false);
        }
        if (_coalLumpMesh is not null && _coalLumps.Count > 0)
        {
            DrawInstancedBatch(_coalLumpMesh, _coalLumps, isShadowPass: false);
        }

        // 7. Instanced Unlit Pipe Flow Particles, Conveyor Highlights & Directional Flow Chevrons
        _glUniform1f!(_uUnlit, 1.0f);
        _glUniform1f!(_uTintMix, 1.0f);
        if (_unitBox is not null && _unlitBoxes.Count > 0)
        {
            DrawInstancedBatch(_unitBox, _unlitBoxes, isShadowPass: false);
        }
        if (_unitCone is not null && _unlitCones.Count > 0)
        {
            DrawInstancedBatch(_unitCone, _unlitCones, isShadowPass: false);
        }

        // 8. Transparent Overlays: Live 3D Fill-Level Indicators, Selection Sleeves, Volumetric Dust/Spray Plumes & Instanced Rings
        _glDepthMask!(0);
        if (_unitCylinder is not null && _fillCylinders.Count > 0)
        {
            _glUniform1f!(_uUnlit, 0.25f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawInstancedBatch(_unitCylinder, _fillCylinders, isShadowPass: false);
        }
        if (_unitBox is not null && _softHighlightBoxes.Count > 0)
        {
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawInstancedBatch(_unitBox, _softHighlightBoxes, isShadowPass: false);
        }
        if (_unitBox is not null && _softParticleBoxes.Count > 0)
        {
            _glUniform1f!(_uUnlit, 2.0f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawInstancedBatch(_unitBox, _softParticleBoxes, isShadowPass: false);
        }
        if (_unitRing is not null && _ringInstances.Count > 0)
        {
            _glUniform1f!(_uUnlit, 1.0f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawInstancedBatch(_unitRing, _ringInstances, isShadowPass: false);
        }
        _glDepthMask!(1);

        // =====================================================================
        // PASS 3: MSAA Blit + Post-FX SSAO, Bloom & ACES Tone Mapping into fb
        // =====================================================================
        if (runPostPass)
        {
            _glBindFramebuffer!(GL_READ_FRAMEBUFFER, _msaaFbo);
            _glBindFramebuffer!(GL_DRAW_FRAMEBUFFER, _postFbo);
            _glBlitFramebuffer!(0, 0, pw, ph, 0, 0, pw, ph, GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT, GL_NEAREST);

            _glBindFramebuffer!(GL_FRAMEBUFFER, (uint)fb);
            _glViewport!(0, 0, pw, ph);
            _glDisable!(GL_DEPTH_TEST);
            _glDisable!(GL_BLEND);
            _glDepthMask!(0);

            _glUseProgram!(_postProgram);
            _glActiveTexture!(GL_TEXTURE0);
            _glBindTexture!(GL_TEXTURE_2D, _postColorTex);
            _glUniform1i!(_uPostColorTex, 0);

            _glActiveTexture!(GL_TEXTURE1);
            _glBindTexture!(GL_TEXTURE_2D, _postDepthTex);
            _glUniform1i!(_uPostDepthTex, 1);

            _glUniform2f?.Invoke(_uPostTexelSize, 1f / pw, 1f / ph);
            _glUniform1f!(_uPostEnableAo, fx.EnableAmbientOcclusion ? 1f : 0f);
            _glUniform1f?.Invoke(_uPostEnableCas, fx.EnableContrastSharpening ? 1f : 0f);
            _glUniform1f?.Invoke(_uPostDecodeHdr, encodeSqrtHdr ? 1f : 0f);
            _glUniform1f!(_uPostIsOrtho, fx.IsOrthographic ? 1f : 0f);
            _glUniform1f!(_uPostNear, fx.IsOrthographic ? 0.1f : 0.2f);
            _glUniform1f!(_uPostFar, 140.0f);
            if (!Matrix4x4.Invert(viewProj, out var invViewProj))
                invViewProj = Matrix4x4.Identity;
            SetMatrix(_uPostInvViewProj, invViewProj);

            _glBindVertexArray!(_postVao);
            _glDrawArrays!(GL_TRIANGLES, 0, 3);
            _frameDrawCalls++;

            _glDepthMask!(1);
            _glEnable!(GL_DEPTH_TEST);
        }
        else if (_msaaReady)
        {
            _glBindFramebuffer!(GL_READ_FRAMEBUFFER, _msaaFbo);
            _glBindFramebuffer!(GL_DRAW_FRAMEBUFFER, (uint)fb);
            _glBlitFramebuffer!(0, 0, pw, ph, 0, 0, pw, ph, GL_COLOR_BUFFER_BIT, GL_NEAREST);
            _glBindFramebuffer!(GL_FRAMEBUFFER, (uint)fb);
        }

        LastFrameDrawCalls = _frameDrawCalls;

        // Capture final resolved, anti-aliased, SSAO + PBR + tone-mapped framebuffer
        if (CaptureRequested)
        {
            CaptureFramePixels(pw, ph);
        }

        _glActiveTexture?.Invoke(GL_TEXTURE1);
        _glBindTexture?.Invoke(GL_TEXTURE_2D, 0);
        _glActiveTexture?.Invoke(GL_TEXTURE0);
        _glBindTexture?.Invoke(GL_TEXTURE_2D, 0);
        _glBindVertexArray!(0);
        _glUseProgram!(0);
    }

    private void CaptureFramePixels(int pw, int ph)
    {
        if (_glReadPixels is null || pw <= 0 || ph <= 0)
            return;

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

    private void BuildInstancedSceneBatches(
        Dictionary<string, EquipmentItem> eqMap,
        IReadOnlyDictionary<string, ProcessFlowState> flowMap,
        LoaderPose loaderPose,
        Unity3DEffectSettings fx,
        string? activeSelected,
        string? hoveredId)
    {
        _groundPadBoxes.Clear();
        _casterBoxes.Clear();
        _casterCylinders.Clear();
        _coalLumps.Clear();
        _unlitBoxes.Clear();
        _unlitCones.Clear();
        _fillCylinders.Clear();
        _softHighlightBoxes.Clear();
        _softParticleBoxes.Clear();
        _ringInstances.Clear();

        // 1. Concrete Foundation Pads + Stage 1-2 Elevated ROM Hill Terraces & Retaining Wall
        foreach (var pad in Plant3DSceneData.FoundationPads)
        {
            var padMat = Matrix4x4.CreateScale(pad.Size) * Matrix4x4.CreateTranslation(pad.Center);
            _groundPadBoxes.Add(new InstanceItem(padMat, 0.14f, 0.18f, 0.25f, 1.0f, 0.05f, 0.88f));

            var trimMat = Matrix4x4.CreateScale(pad.Size.X, 0.04f, 0.12f)
                        * Matrix4x4.CreateTranslation(pad.Center.X, 0.02f, pad.Center.Z + pad.Size.Z * 0.5f - 0.06f);
            _groundPadBoxes.Add(new InstanceItem(trimMat, pad.AccentColor.R / 255f, pad.AccentColor.G / 255f, pad.AccentColor.B / 255f, 1.0f, 0.25f, 0.45f));
        }

        // Multi-tier Stage 1 ROM Hill embankment (SP-001, FEL-101, BN-101 atop +4.35 m plateau)
        _groundPadBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(7.8f, 2.20f, 13.0f) * Matrix4x4.CreateTranslation(-13.8f, 1.10f, 13.8f),
            0.22f, 0.19f, 0.15f, 1.0f, 0.04f, 0.92f));
        _groundPadBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(6.8f, 1.20f, 12.2f) * Matrix4x4.CreateTranslation(-13.8f, 2.80f, 13.4f),
            0.25f, 0.21f, 0.17f, 1.0f, 0.04f, 0.90f));
        _groundPadBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(5.8f, 0.95f, 11.4f) * Matrix4x4.CreateTranslation(-13.8f, 3.875f, 13.0f),
            0.24f, 0.26f, 0.30f, 1.0f, 0.08f, 0.85f));

        // Shadow-casting North concrete retaining wall at Z=7.25 + VF-101 elevated structural steel tower
        _casterBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(6.2f, 4.38f, 0.36f) * Matrix4x4.CreateTranslation(-13.8f, 2.19f, 7.25f),
            0.32f, 0.36f, 0.42f, 1.0f, 0.10f, 0.80f));
        foreach (float xCol in new[] { -14.85f, -12.75f })
        {
            foreach (float zCol in new[] { 5.25f, 7.05f })
            {
                _casterBoxes.Add(new InstanceItem(
                    Matrix4x4.CreateScale(0.18f, 4.55f, 0.18f) * Matrix4x4.CreateTranslation(xCol, 2.275f, zCol),
                    0.28f, 0.35f, 0.44f, 1.0f, 0.75f, 0.40f));
            }
        }
        _casterBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(2.28f, 0.16f, 1.98f) * Matrix4x4.CreateTranslation(-13.8f, 4.47f, 6.15f),
            0.28f, 0.35f, 0.44f, 1.0f, 0.75f, 0.40f));
        _casterBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(2.20f, 0.12f, 1.90f) * Matrix4x4.CreateTranslation(-13.8f, 2.30f, 6.15f),
            0.26f, 0.32f, 0.40f, 1.0f, 0.72f, 0.42f));

        // 2. Machine Mechanical Sub-Parts & Live Fill Levels
        foreach (var p in Plant3DSceneData.MachineNodes)
        {
            eqMap.TryGetValue(p.LinkedScadaId, out var eq);
            var flow = flowMap.GetValueOrDefault(p.Id);
            bool running = flow is not null ? flow.IsRunning : (eq is null || eq.Status != EquipmentStatus.Stopped);

            if (p.Id == "JC-101")
            {
                AppendJawCrusherMechanics(p.Position, running && fx.AnimateConveyors);
            }
            else if (p.Id == "DRC-101")
            {
                AppendDoubleRollCrusherMechanics(p.Position, running && fx.AnimateConveyors);
            }
            else if (p.HasRotatingTop)
            {
                float ang = (fx.AnimateConveyors && running) ? (float)(SimTime * (p.Id == "TK-201" ? 0.45 : 1.8)) : 0.2f;
                var rotMat = Matrix4x4.CreateScale(p.RotatingTopLength, 0.14f, 0.24f)
                           * Matrix4x4.CreateRotationY(ang)
                           * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.RotatingTopY, p.Position.Z);
                _casterBoxes.Add(new InstanceItem(rotMat, 0.22f, 0.78f, 0.98f, 1.0f, 0.72f, 0.30f));

                if (p.Id == "TK-201")
                {
                    AppendThickenerRakeTruss(p.Position, ang);
                }
            }

            if (p.HasFillLevel)
            {
                float fillFrac = eq is not null ? Math.Clamp((float)(eq.LoadPercent / 100.0), 0.12f, 0.95f) : 0.60f;
                if (p.Id == "BN-101") fillFrac = Math.Clamp(fillFrac + loaderPose.BinFillPulse, 0.15f, 0.98f);

                bool isCoalBin = p.Id is "BN-101" or "BN-201";
                float fillH = p.Id == "TK-201"
                    ? p.FillMaxHeight * Math.Clamp(0.76f + 0.18f * fillFrac, 0.76f, 0.96f)
                    : p.FillMaxHeight * fillFrac;
                var fillColor = p.Id == "BN-101" ? Color.Parse("#1C1E22")
                              : p.Id == "BN-201" ? Color.Parse("#12151A")
                              : p.Id == "TK-301" ? Color.Parse("#14B8A6")
                              : Color.Parse("#1D7CB8");

                var fm = Matrix4x4.CreateScale(p.FillRadius, fillH, p.FillRadius)
                       * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.FillBaseY + fillH * 0.5f, p.Position.Z);
                _fillCylinders.Add(new InstanceItem(
                    fm,
                    fillColor.R / 255f,
                    fillColor.G / 255f,
                    fillColor.B / 255f,
                    isCoalBin ? 0.78f : (p.Id == "TK-301" ? 0.72f : 0.52f),
                    isCoalBin ? 0.06f : 0.14f,
                    isCoalBin ? 0.84f : 0.17f));
            }

            AppendMachineStatusBeacon(p, eq, loaderPose);
        }

        // MX-301 Dense-Medium Mixing Box agitated slurry surface (matches isWater predicate)
        _casterBoxes.Add(new InstanceItem(
            Matrix4x4.CreateScale(1.88f, 0.04f, 1.88f) * Matrix4x4.CreateTranslation(2.30f, 1.84f, -8.30f),
            0.078f, 0.239f, 0.349f, 1.0f, 0.14f, 0.20f));

        // Instrumentation beacon LED lenses (VT-102, LT-201, DT-301, PT-301) with 2.4x HDR boost
        foreach (var sensorPos in new[]
        {
            new Vector3(0.25f, 2.58f, 1.38f),
            new Vector3(-0.55f, 2.26f, -15.25f),
            new Vector3(-2.50f, 1.66f, -10.20f),
            new Vector3(2.30f, 3.28f, -8.30f)
        })
        {
            var lm = Matrix4x4.CreateScale(0.085f, 0.095f, 0.085f) * Matrix4x4.CreateTranslation(sensorPos);
            _unlitBoxes.Add(new InstanceItem(lm, 0.16f, 0.96f, 0.62f, 1.0f, 1.0f, 0.25f));
        }

        void AddCoalChunk(Vector3 pos, float size, Color col, float metallic, float roughness, int seed, float rotAnim = 0f)
        {
            float shade = 0.82f + (seed % 5) * 0.10f;
            float sx = size * (1.28f - 0.12f * (seed % 2));
            float sy = size * (0.76f + 0.14f * (seed % 3));
            float sz = size * (1.06f + 0.10f * ((seed + 1) % 2));
            var mat = Matrix4x4.CreateScale(sx, sy, sz)
                    * Matrix4x4.CreateRotationX(seed * 0.57f + rotAnim * 0.6f)
                    * Matrix4x4.CreateRotationY(seed * 0.93f + rotAnim)
                    * Matrix4x4.CreateRotationZ(seed * 0.41f)
                    * Matrix4x4.CreateTranslation(pos);
            _coalLumps.Add(new InstanceItem(
                mat,
                MathF.Min(1f, col.R / 255f * shade),
                MathF.Min(1f, col.G / 255f * shade),
                MathF.Min(1f, col.B / 255f * shade),
                1.0f, metallic, roughness));
        }

        // 2b. Surface Coal Granulometry on Stockpiles (Big ROM Boulders at SP-001 vs Small Product/Discard at SP-301/SP-302)
        var romCol = Plant3DSceneData.GetMaterialColor(CoalMaterialClass.RawRom);
        for (int i = 0; i < 14; i++)
        {
            float size = 0.34f + (i % 4) * 0.045f;
            float angle = -MathF.PI * 0.5f + (i - 6.5f) * 0.32f;
            float rFrac = i >= 10 ? (1.02f + (i - 10) * 0.05f) : (0.58f + (i % 4) * 0.12f);
            float rad = 2.64f * rFrac;
            float slopeY = i >= 10 ? size * 0.42f : MathF.Max(0.18f, (1f - rFrac) * 2.10f);
            var bp = new Vector3(-13.8f + MathF.Cos(angle) * rad, 4.35f + slopeY, 16.4f + MathF.Sin(angle) * rad);
            AddCoalChunk(bp, size, romCol, 0.06f, 0.84f, 10 + i);
        }

        var prodCol = Plant3DSceneData.GetMaterialColor(CoalMaterialClass.CleanProduct);
        for (int i = 0; i < 16; i++)
        {
            float size = 0.075f + (i % 3) * 0.01f;
            float angle = i * (MathF.PI * 2f / 16f) + 0.25f;
            float rFrac = 0.35f + (i % 4) * 0.20f;
            float rad = 2.48f * rFrac;
            float slopeY = MathF.Max(0.08f, (1f - rFrac) * 1.92f);
            var bp = new Vector3(-9.8f + MathF.Cos(angle) * rad, slopeY, -5.2f + MathF.Sin(angle) * rad);
            AddCoalChunk(bp, size, prodCol, 0.08f, 0.32f, 40 + i);
        }

        var discCol = Plant3DSceneData.GetMaterialColor(CoalMaterialClass.RefuseDiscard);
        for (int i = 0; i < 12; i++)
        {
            float size = 0.085f + (i % 3) * 0.012f;
            float angle = i * (MathF.PI * 2f / 12f) + 0.40f;
            float rFrac = 0.40f + (i % 3) * 0.24f;
            float rad = 2.24f * rFrac;
            float slopeY = MathF.Max(0.08f, (1f - rFrac) * 1.70f);
            var bp = new Vector3(-10.6f + MathF.Cos(angle) * rad, slopeY, -8.2f + MathF.Sin(angle) * rad);
            AddCoalChunk(bp, size, discCol, 0.07f, 0.38f, 70 + i);
        }

        // 3. Loader Bucket Load & Dump ROM Coal Boulders
        if (loaderPose.BoomAngleDegrees > 1.5f || loaderPose.IsDumpingCoal)
        {
            const float deg = MathF.PI / 180f;
            var chassisWorld = Matrix4x4.CreateRotationZ(loaderPose.PitchDegrees * deg)
                             * Matrix4x4.CreateRotationY(loaderPose.YawDegrees * deg)
                             * Matrix4x4.CreateTranslation(loaderPose.ChassisPosition);
            var glbBucketWorld = Matrix4x4.CreateRotationZ((loaderPose.BoomAngleDegrees + loaderPose.BucketDumpAngleDegrees * 0.45f) * deg)
                               * Matrix4x4.CreateTranslation(1.30f, 1.38f, 0f)
                               * chassisWorld;

            if (!loaderPose.IsDumpingCoal)
            {
                for (int i = 0; i < 6; i++)
                {
                    float zOff = -0.58f + (i % 3) * 0.56f;
                    float xOff = 1.98f + (i / 3) * 0.28f;
                    float yOff = -0.78f + (i % 2) * 0.16f;
                    var worldPos = Vector3.Transform(new Vector3(xOff, yOff, zOff), glbBucketWorld);
                    AddCoalChunk(worldPos, 0.32f + (i % 3) * 0.045f, romCol, 0.06f, 0.82f, 100 + i);
                }
            }
            else
            {
                var dumpOrigin = Vector3.Transform(new Vector3(2.45f, -0.95f, 0f), glbBucketWorld);
                var binTarget = new Vector3(-13.8f, 6.50f, 8.8f);
                for (int i = 0; i < 9; i++)
                {
                    float frac = (float)((SimTime * 2.8 + i / 9.0) % 1.0);
                    var p = Vector3.Lerp(dumpOrigin, binTarget, frac);
                    p.X += MathF.Sin(i * 2.1f) * 0.24f;
                    p.Z += MathF.Cos(i * 1.7f) * 0.24f;
                    p.Y -= frac * frac * 0.35f;
                    AddCoalChunk(p, 0.34f + (i % 3) * 0.05f, romCol, 0.06f, 0.80f, 120 + i, (float)SimTime * 3.0f);
                }
            }
        }

        // 4. Conveyors: Pulleys, Keys, Cleats, Ballistic Coal Lumps, Highlights, Chevrons
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

            if (isSel || isHov)
            {
                var hlColor = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
                var mid = (c.TailPos + c.HeadPos) * 0.5f;
                var hlMat = Matrix4x4.CreateScale(length, 0.08f, c.Width + 0.28f)
                          * beltRot
                          * Matrix4x4.CreateTranslation(mid.X, mid.Y + 0.04f, mid.Z);
                _unlitBoxes.Add(new InstanceItem(hlMat, hlColor.R / 255f, hlColor.G / 255f, hlColor.B / 255f, 0.85f, 0f, 0.5f));
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
                _casterCylinders.Add(new InstanceItem(pScale, 0.88f, 0.48f, 0.08f, 1.0f, 0.72f, 0.34f));

                var keyMat = Matrix4x4.CreateScale(0.50f, c.Width * 1.15f, 0.07f) * pOrient;
                _casterBoxes.Add(new InstanceItem(keyMat, 0.16f, 0.22f, 0.30f, 1.0f, 0.80f, 0.35f));
            }

            // Moving Belt Cleats
            const float cleatSpacing = 0.72f;
            int cleatCount = Math.Max(3, (int)(length / cleatSpacing));
            float phase = (float)((SimTime * speed * 1.45) % cleatSpacing);
            for (int i = 0; i < cleatCount; i++)
            {
                float distAlong = i * cleatSpacing + phase;
                if (distAlong > length) continue;
                var p = c.TailPos + dir * distAlong;
                var cm = Matrix4x4.CreateScale(0.08f, 0.05f, c.Width * 0.88f)
                       * beltRot
                       * Matrix4x4.CreateTranslation(p.X, p.Y + 0.08f, p.Z);
                _casterBoxes.Add(new InstanceItem(cm, 0.28f, 0.33f, 0.40f, 1.0f, 0.15f, 0.82f));
            }

            // Continuous Troughed Coal Burden Ribbon + Multi-Cluster Faceted Coal Lumps + Flow Chevrons
            if (flow is not null && flow.NormalizedRate > 0.02)
            {
                var matCol = Plant3DSceneData.GetMaterialColor(c.MaterialClass);
                var pbr = Plant3DSceneData.GetMaterialPbrParams(c.MaterialClass);
                float lumpSize = Plant3DSceneData.GetMaterialLumpSize(c.MaterialClass);
                int subCount = Plant3DSceneData.GetMaterialSubClusterCount(c.MaterialClass);
                float burdenH = Plant3DSceneData.ComputeBeltBurdenThickness(c.MaterialClass, flow.NormalizedRate);
                if (burdenH > 0.005f)
                {
                    var mid = (c.TailPos + c.HeadPos) * 0.5f;
                    var burdenMat = Matrix4x4.CreateScale(length * 0.96f, burdenH, c.Width * 0.54f)
                                  * beltRot
                                  * Matrix4x4.CreateTranslation(mid.X, mid.Y + 0.045f + burdenH * 0.5f, mid.Z);
                    _casterBoxes.Add(new InstanceItem(burdenMat, matCol.R / 255f * 0.90f, matCol.G / 255f * 0.90f, matCol.B / 255f * 0.90f, 1.0f, pbr.Metallic, pbr.Roughness));
                }

                float dropLen = Vector3.Distance(c.HeadPos, c.GetChuteBottomPos());
                float totalLen = length + dropLen;
                int parcelCount = Math.Max(8, (int)(totalLen / 0.55f));
                var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));

                for (int i = 0; i < parcelCount; i++)
                {
                    float u = (float)((SimTime * 0.24 * Math.Max(0.2, speed) + (double)i / parcelCount) % 1.0);
                    var basePos = Plant3DSceneData.SampleConveyorCoalTrajectory(c, u);
                    float jitterScale = u <= 0.82f ? 1.0f : Math.Max(0.15f, 1.0f - (u - 0.82f) / 0.18f);

                    for (int s = 0; s < subCount; s++)
                    {
                        float lateralFrac = subCount == 1
                            ? MathF.Sin(i * 3.1f) * 0.35f
                            : (-0.5f + s / (float)(subCount - 1)) * 0.72f + MathF.Sin(i * 2.7f + s * 1.9f) * 0.14f;
                        var pos = basePos + right * (lateralFrac * c.Width * 0.36f * jitterScale);
                        if (subCount > 1)
                            pos += dir * (MathF.Cos(i * 1.9f + s * 2.3f) * 0.10f * (u <= 0.82f ? 1f : 0f));
                        if (u <= 0.82f)
                            pos.Y += 0.085f + burdenH * 0.65f + lumpSize * 0.34f;

                        float variantSize = lumpSize * (0.88f + ((i + s) % 3) * 0.12f);
                        AddCoalChunk(pos, variantSize, matCol, pbr.Metallic, pbr.Roughness, i * 7 + s * 3);
                    }
                }

                // Curved feed chute coal parcels (e.g. CV-106)
                if (c.FeedChuteWaypoints is { Length: >= 2 })
                {
                    for (int fc = 0; fc < 4; fc++)
                    {
                        float ft = (float)((SimTime * 0.35 * Math.Max(0.2, speed) + fc / 4.0) % 1.0);
                        var fp = Plant3DSceneData.SamplePolyline(c.FeedChuteWaypoints, ft);
                        AddCoalChunk(fp, lumpSize * 0.95f, matCol, pbr.Metallic, pbr.Roughness, 500 + fc);
                    }
                }

                if (fx.ShowFlowDirection && _unitCone is not null)
                {
                    int arrowCount = Math.Max(2, (int)(length / 2.8f));
                    for (int a = 0; a < arrowCount; a++)
                    {
                        float t = (float)((SimTime * 0.35 * Math.Max(0.25, speed) + (double)a / arrowCount) % 1.0);
                        var ap = Vector3.Lerp(c.TailPos, c.HeadPos, t);
                        ap.Y += 0.65f;
                        var am = Matrix4x4.CreateScale(0.22f, 0.42f, 0.22f)
                               * Matrix4x4.CreateRotationZ(-MathF.PI * 0.5f)
                               * beltRot
                               * Matrix4x4.CreateTranslation(ap);
                        _unlitCones.Add(new InstanceItem(am, 0.22f, 0.82f, 0.98f, 0.88f, 0f, 0.5f));
                    }
                }
            }
        }

        // 5. Pipe Flow Particles
        if (fx.ShowFlowParticles)
        {
            foreach (var route in Plant3DSceneData.PipeRoutes)
            {
                eqMap.TryGetValue(route.SourceEquipmentId, out var srcEq);
                double speed = Plant3DSceneData.GetFlowSpeedFactor(srcEq);
                if (speed <= 0.0) continue;

                var c = Plant3DSceneData.GetMediumColor(route.Medium);
                const int count = 6;
                for (int i = 0; i < count; i++)
                {
                    float t = (float)((SimTime * 0.30 * speed + (double)i / count) % 1.0);
                    var pos = Plant3DSceneData.SamplePolyline(route.Waypoints, t);
                    var pm = Matrix4x4.CreateScale(0.16f) * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.06f, pos.Z);
                    _unlitBoxes.Add(new InstanceItem(pm, c.R / 255f, c.G / 255f, c.B / 255f, 0.95f, 0f, 0.4f));
                }
            }
        }

        // 5b. Individual Sub-Equipment & Pipe Hover/Selection Highlights
        foreach (var sub in Plant3DSceneData.SubEquipmentNodes)
        {
            bool isSel = string.Equals(activeSelected, sub.Id, StringComparison.OrdinalIgnoreCase);
            bool isHov = string.Equals(hoveredId, sub.Id, StringComparison.OrdinalIgnoreCase);
            if (!isSel && !isHov) continue;

            var hc = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
            float r = hc.R / 255f, g = hc.G / 255f, b = hc.B / 255f;
            var size = new Vector3(sub.HalfExtents.X * 2f + 0.10f, sub.HalfExtents.Y * 2f + 0.10f, sub.HalfExtents.Z * 2f + 0.10f);
            var center = new Vector3(sub.Position.X, sub.Position.Y + sub.HalfExtents.Y, sub.Position.Z);
            _softHighlightBoxes.Add(new InstanceItem(
                Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center),
                r, g, b, isSel ? 0.30f : 0.20f, 0f, 0.5f));

            // Bright rim bars on top & bottom edges
            foreach (float yEdge in new[] { center.Y - size.Y * 0.5f, center.Y + size.Y * 0.5f })
            {
                foreach (float zEdge in new[] { center.Z - size.Z * 0.5f, center.Z + size.Z * 0.5f })
                    _unlitBoxes.Add(new InstanceItem(Matrix4x4.CreateScale(size.X, 0.035f, 0.035f) * Matrix4x4.CreateTranslation(center.X, yEdge, zEdge), r, g, b, 0.95f, 0f, 0.5f));
                foreach (float xEdge in new[] { center.X - size.X * 0.5f, center.X + size.X * 0.5f })
                    _unlitBoxes.Add(new InstanceItem(Matrix4x4.CreateScale(0.035f, 0.035f, size.Z) * Matrix4x4.CreateTranslation(xEdge, yEdge, center.Z), r, g, b, 0.95f, 0f, 0.5f));
            }
        }

        foreach (var route in Plant3DSceneData.PipeRoutes)
        {
            bool isSel = string.Equals(activeSelected, route.Id, StringComparison.OrdinalIgnoreCase);
            bool isHov = string.Equals(hoveredId, route.Id, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(route.Id) || (!isSel && !isHov)) continue;

            var hc = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
            float sleeve = route.Radius * 2f + 0.12f;
            for (int w = 0; w < route.Waypoints.Length - 1; w++)
            {
                var a = route.Waypoints[w];
                var bPt = route.Waypoints[w + 1];
                var d = bPt - a;
                float len = d.Length();
                if (len < 1e-3f) continue;
                var dirN = d / len;
                var axis = Vector3.Cross(Vector3.UnitX, dirN);
                float dot = Math.Clamp(Vector3.Dot(Vector3.UnitX, dirN), -1f, 1f);
                Quaternion q = axis.LengthSquared() < 1e-8f
                    ? (dot > 0f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI))
                    : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot));
                var m = Matrix4x4.CreateScale(len + sleeve, sleeve, sleeve)
                      * Matrix4x4.CreateFromQuaternion(q)
                      * Matrix4x4.CreateTranslation((a + bPt) * 0.5f);
                _softHighlightBoxes.Add(new InstanceItem(m, hc.R / 255f, hc.G / 255f, hc.B / 255f, isSel ? 0.55f : 0.38f, 0f, 0.5f));
            }
        }

        // 6. Fugitive Dust & Desliming Water Spray Soft Particles
        if (fx.ShowDustAndSprayPlumes)
        {
            foreach (var sp in Plant3DSceneData.ComputeDustAndSprayParticles(flowMap, loaderPose, SimTime))
            {
                var sm = Matrix4x4.CreateScale(sp.Scale) * Matrix4x4.CreateTranslation(sp.Position);
                _softParticleBoxes.Add(new InstanceItem(sm, sp.R, sp.G, sp.B, sp.Alpha, 0f, sp.IsWaterSpray ? 0.25f : 0.95f));
            }
        }

        // 7. Instanced Selection, Hover & Alarm Rings (1 draw call max)
        float pulse = 0.55f + 0.45f * MathF.Sin((float)(SimTime * 4.5));
        foreach (var p in Plant3DSceneData.InteractiveNodes)
        {
            eqMap.TryGetValue(p.LinkedScadaId, out var eq);
            var pos = p.Id == "FEL-101" ? loaderPose.ChassisPosition : p.Position;
            bool isPrimaryMachine = !p.IsConveyor && !p.IsSubComponent && !p.IsPipe;
            float baseR = p.IsConveyor ? 1.15f
                : p.IsPipe ? 0.55f
                : MathF.Max(p.HalfExtents.X, p.HalfExtents.Z) + 0.32f;

            if (isPrimaryMachine && fx.ShowAlarmHalos && eq is not null && eq.Status is EquipmentStatus.Warning or EquipmentStatus.Fault)
            {
                var ac = eq.Status == EquipmentStatus.Fault ? Color.Parse("#EF4444") : Color.Parse("#F59E0B");
                var rm = Matrix4x4.CreateScale(baseR + 0.25f * pulse, 1f, baseR + 0.25f * pulse)
                       * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.08f, pos.Z);
                _ringInstances.Add(new InstanceItem(rm, ac.R / 255f, ac.G / 255f, ac.B / 255f, 0.42f + 0.42f * pulse, 0f, 0.5f));
            }

            bool isSel = string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase);
            bool isHov = string.Equals(hoveredId, p.Id, StringComparison.OrdinalIgnoreCase);
            if (isSel || isHov)
            {
                var sc = isSel ? Color.Parse("#38BDF8") : Color.Parse("#93C5FD");
                var rm = Matrix4x4.CreateScale(baseR + 0.16f, 1f, baseR + 0.16f)
                       * Matrix4x4.CreateTranslation(pos.X, pos.Y + 0.10f, pos.Z);
                _ringInstances.Add(new InstanceItem(rm, sc.R / 255f, sc.G / 255f, sc.B / 255f, isSel ? 0.92f : 0.62f, 0f, 0.5f));
            }
        }
    }

    private void AppendThickenerRakeTruss(Vector3 center, float angle)
    {
        var rotY = Matrix4x4.CreateRotationY(angle);
        var xlate = Matrix4x4.CreateTranslation(center);

        // Central vertical torque shaft connecting drive head down to rake hub
        var shaftMat = Matrix4x4.CreateScale(0.13f, 2.12f, 0.13f)
                     * Matrix4x4.CreateTranslation(center.X, center.Y + 1.15f, center.Z);
        _casterCylinders.Add(new InstanceItem(shaftMat, 0.72f, 0.76f, 0.84f, 1.0f, 0.88f, 0.24f));

        // Submerged upper rake truss chord at Y = 0.95 and lower scraper chord at Y = 0.45
        var topChord = Matrix4x4.CreateScale(4.75f, 0.09f, 0.13f)
                     * Matrix4x4.CreateTranslation(0f, 0.95f, 0f) * rotY * xlate;
        _casterBoxes.Add(new InstanceItem(topChord, 0.88f, 0.68f, 0.14f, 1.0f, 0.70f, 0.36f));

        var botChord = Matrix4x4.CreateScale(4.60f, 0.08f, 0.11f)
                     * Matrix4x4.CreateTranslation(0f, 0.45f, 0f) * rotY * xlate;
        _casterBoxes.Add(new InstanceItem(botChord, 0.44f, 0.52f, 0.62f, 1.0f, 0.78f, 0.34f));

        foreach (float rx in new[] { -2.10f, -1.25f, 1.25f, 2.10f })
        {
            var strutMat = Matrix4x4.CreateScale(0.07f, 0.50f, 0.07f)
                         * Matrix4x4.CreateTranslation(rx, 0.70f, 0f) * rotY * xlate;
            _casterBoxes.Add(new InstanceItem(strutMat, 0.85f, 0.65f, 0.14f, 1.0f, 0.70f, 0.36f));

            var bladeMat = Matrix4x4.CreateScale(0.08f, 0.18f, 0.32f)
                         * Matrix4x4.CreateRotationY(0.55f)
                         * Matrix4x4.CreateTranslation(rx, 0.36f, 0f) * rotY * xlate;
            _casterBoxes.Add(new InstanceItem(bladeMat, 0.32f, 0.38f, 0.46f, 1.0f, 0.82f, 0.35f));
        }
    }

    private void AppendMachineStatusBeacon(Plant3DNode p, EquipmentItem? eq, LoaderPose loaderPose)
    {
        var status = eq?.Status ?? EquipmentStatus.Running;
        float lr, lg, lb, hdrFlag;
        if (status == EquipmentStatus.Fault)
        {
            bool flash = MathF.Sin((float)(SimTime * 9.0)) > -0.2f;
            lr = flash ? 1.00f : 0.42f; lg = 0.20f; lb = 0.16f;
            hdrFlag = flash ? 1.0f : 0.0f;
        }
        else if (status == EquipmentStatus.Warning)
        {
            float pulse = 0.72f + 0.28f * MathF.Sin((float)(SimTime * 5.0));
            lr = 1.00f * pulse; lg = 0.72f * pulse; lb = 0.12f * pulse;
            hdrFlag = 1.0f;
        }
        else if (status is EquipmentStatus.Stopped or EquipmentStatus.Offline)
        {
            lr = 0.36f; lg = 0.42f; lb = 0.50f;
            hdrFlag = 0.0f;
        }
        else
        {
            lr = 0.16f; lg = 0.96f; lb = 0.58f;
            hdrFlag = 1.0f;
        }

        if (p.Id == "FEL-101")
        {
            const float deg = MathF.PI / 180f;
            var chassisWorld = Matrix4x4.CreateRotationZ(loaderPose.PitchDegrees * deg)
                             * Matrix4x4.CreateRotationY(loaderPose.YawDegrees * deg)
                             * Matrix4x4.CreateTranslation(loaderPose.ChassisPosition);
            var lensWorld = Matrix4x4.CreateScale(0.14f, 0.15f, 0.14f)
                          * Matrix4x4.CreateTranslation(0.28f, 3.08f, 0f)
                          * chassisWorld;
            _unlitBoxes.Add(new InstanceItem(lensWorld, lr, lg, lb, 1.0f, hdrFlag, 0.25f));
            return;
        }

        float mastH = 0.42f;
        Vector3 anchor = p.Id switch
        {
            "SP-001" => p.Position + new Vector3(-2.30f, 0f, -1.50f),
            "SP-301" => p.Position + new Vector3(-2.05f, 0f, 1.55f),
            "SP-302" => p.Position + new Vector3(-1.95f, 0f, -1.45f),
            "BN-101" => p.Position + new Vector3(1.22f, 3.56f, -1.22f),
            "VF-101" => p.Position + new Vector3(-0.68f, 1.45f, 0f),
            "JC-101" => p.Position + new Vector3(-1.55f, 2.78f, 1.35f),
            "DRC-101" => p.Position + new Vector3(-1.45f, 4.10f, -1.25f),
            "SS-101" => p.Position + new Vector3(-2.05f, 3.95f, 1.45f),
            "BN-201" => p.Position + new Vector3(-0.25f, 4.59f, 0f),
            "DS-201" => p.Position + new Vector3(1.75f, 2.45f, -1.28f),
            "WP-301" => p.Position + new Vector3(-3.30f, 3.15f, 1.55f),
            "MS-301" => p.Position + new Vector3(0.80f, 1.18f, -0.60f),
            "TK-301" => p.Position + new Vector3(-1.35f, 2.61f, 0f),
            "TK-201" => p.Position + new Vector3(2.95f, 2.71f, 0.32f),
            "PP-201" or "PP-202" => p.Position + new Vector3(-0.32f, 0.95f, 0f),
            "PP-203" => p.Position + new Vector3(0.32f, 0.95f, 0f),
            _ => p.Position + new Vector3(0f, p.HalfExtents.Y * 2f, 0f)
        };
        if (p.EquipmentType.Contains("Stockpile", StringComparison.OrdinalIgnoreCase))
            mastH = 1.95f;

        var mastMat = Matrix4x4.CreateScale(0.032f, mastH, 0.032f)
                    * Matrix4x4.CreateTranslation(anchor.X, anchor.Y + mastH * 0.5f, anchor.Z);
        _casterCylinders.Add(new InstanceItem(mastMat, 0.20f, 0.25f, 0.32f, 1.0f, 0.78f, 0.36f));

        var lensMat = Matrix4x4.CreateScale(0.13f, 0.15f, 0.13f)
                    * Matrix4x4.CreateTranslation(anchor.X, anchor.Y + mastH + 0.075f, anchor.Z);
        _unlitBoxes.Add(new InstanceItem(lensMat, lr, lg, lb, 1.0f, hdrFlag, 0.25f));
    }

    private void AppendJawCrusherMechanics(Vector3 pos, bool running)
    {
        float flywheelAngle = running ? (float)(SimTime * 6.5) : 0f;
        foreach (float zOff in new[] { -1.06f, 1.06f })
        {
            var wheelCenter = Matrix4x4.CreateRotationY(flywheelAngle)
                            * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                            * Matrix4x4.CreateTranslation(pos.X, pos.Y + 2.50f, pos.Z + zOff);
            var wheelMat = Matrix4x4.CreateScale(0.58f, 0.18f, 0.58f) * wheelCenter;
            _casterCylinders.Add(new InstanceItem(wheelMat, 0.85f, 0.55f, 0.10f, 1.0f, 0.78f, 0.32f));

            var spokeMat = Matrix4x4.CreateScale(1.08f, 0.20f, 0.12f) * wheelCenter;
            _casterBoxes.Add(new InstanceItem(spokeMat, 0.18f, 0.24f, 0.32f, 1.0f, 0.80f, 0.35f));
        }

        float jawSwing = running ? MathF.Sin((float)(SimTime * 28.27)) * 0.08f : 0f;
        var jawMat = Matrix4x4.CreateScale(0.16f, 1.05f, 1.16f)
                   * Matrix4x4.CreateRotationZ(-0.20f + jawSwing * 1.4f)
                   * Matrix4x4.CreateTranslation(pos.X - 0.12f + jawSwing, pos.Y + 2.48f, pos.Z);
        _casterBoxes.Add(new InstanceItem(jawMat, 0.92f, 0.68f, 0.18f, 1.0f, 0.82f, 0.38f));
    }

    private void AppendDoubleRollCrusherMechanics(Vector3 pos, bool running)
    {
        float rollAngle = running ? (float)(SimTime * 5.2) : 0f;
        for (int r = 0; r < 2; r++)
        {
            float xOff = r == 0 ? -0.36f : 0.36f;
            float dir = r == 0 ? 1f : -1f;
            float ang = rollAngle * dir;

            var rollAxis = Matrix4x4.CreateRotationY(ang)
                         * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                         * Matrix4x4.CreateTranslation(pos.X + xOff, pos.Y + 2.02f, pos.Z);

            var drumMat = Matrix4x4.CreateScale(0.32f, 1.32f, 0.32f) * rollAxis;
            _casterCylinders.Add(new InstanceItem(drumMat, 0.35f, 0.42f, 0.52f, 1.0f, 0.85f, 0.32f));

            for (int tooth = 0; tooth < 4; tooth++)
            {
                float ta = ang + tooth * (MathF.PI * 0.25f);
                var ribMat = Matrix4x4.CreateScale(0.72f, 1.28f, 0.09f)
                           * Matrix4x4.CreateRotationY(ta)
                           * Matrix4x4.CreateRotationX(MathF.PI * 0.5f)
                           * Matrix4x4.CreateTranslation(pos.X + xOff, pos.Y + 2.02f, pos.Z);
                _casterBoxes.Add(new InstanceItem(ribMat, 0.96f, 0.65f, 0.12f, 1.0f, 0.78f, 0.36f));
            }
        }
    }

    private void DrawMachineNodesShadowPass(
        LoaderPose loaderPose,
        Dictionary<string, EquipmentItem> eqMap,
        IReadOnlyDictionary<string, ProcessFlowState> flowMap,
        Unity3DEffectSettings fx)
    {
        const float deg = MathF.PI / 180f;
        foreach (var p in Plant3DSceneData.MachineNodes)
        {
            if (p.Id == "FEL-101")
            {
                var chassisWorld = Matrix4x4.CreateRotationZ(loaderPose.PitchDegrees * deg)
                                 * Matrix4x4.CreateRotationY(loaderPose.YawDegrees * deg)
                                 * Matrix4x4.CreateTranslation(loaderPose.ChassisPosition);
                if (_glbGpuMeshes.TryGetValue("wheel_loader_chassis.glb", out var chassisMesh))
                {
                    SetMatrix(_uShadowModel, chassisWorld);
                    DrawMesh(chassisMesh);
                }
                else if (_unitBox is not null)
                {
                    SetMatrix(_uShadowModel, Matrix4x4.CreateScale(2.3f, 1.05f, 1.45f) * Matrix4x4.CreateTranslation(-0.15f, 0.85f, 0f) * chassisWorld);
                    DrawMesh(_unitBox);
                }

                var glbBucketWorld = Matrix4x4.CreateRotationZ((loaderPose.BoomAngleDegrees + loaderPose.BucketDumpAngleDegrees * 0.45f) * deg)
                                   * Matrix4x4.CreateTranslation(1.30f, 1.38f, 0f)
                                   * chassisWorld;
                if (_glbGpuMeshes.TryGetValue("wheel_loader_bucket.glb", out var bucketMesh))
                {
                    SetMatrix(_uShadowModel, glbBucketWorld);
                    DrawMesh(bucketMesh);
                }
                continue;
            }

            eqMap.TryGetValue(p.LinkedScadaId, out var eq);
            var flow = flowMap.GetValueOrDefault(p.Id);
            bool running = flow is not null ? flow.IsRunning : (eq is null || eq.Status != EquipmentStatus.Stopped);
            var vib = (p.Vibrates && fx.EnableVibrationPhysics && running)
                ? Plant3DSceneData.ComputeScreenVibrationOffset(p.Id, SimTime, eq?.LoadPercent ?? 70.0)
                : Vector3.Zero;

            var model = Matrix4x4.CreateScale(p.Scale)
                      * Matrix4x4.CreateRotationY(p.YawDegrees * deg)
                      * Matrix4x4.CreateTranslation(p.Position.X + vib.X, p.Position.Y + vib.Y, p.Position.Z + vib.Z);

            if (_glbGpuMeshes.TryGetValue(p.GlbFileName, out var gpuMesh))
            {
                SetMatrix(_uShadowModel, model);
                DrawMesh(gpuMesh);
            }
            else if (p.EquipmentType.Contains("Stockpile", StringComparison.OrdinalIgnoreCase) && _unitCone is not null)
            {
                SetMatrix(_uShadowModel, Matrix4x4.CreateScale(p.HalfExtents.X, p.HalfExtents.Y * 2f, p.HalfExtents.Z)
                                       * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.HalfExtents.Y, p.Position.Z));
                DrawMesh(_unitCone);
            }
            else if (_unitBox is not null)
            {
                SetMatrix(_uShadowModel, Matrix4x4.CreateScale(p.HalfExtents.X * 2f, p.HalfExtents.Y * 2f, p.HalfExtents.Z * 2f)
                                       * Matrix4x4.CreateTranslation(p.Position.X + vib.X, p.Position.Y + p.HalfExtents.Y + vib.Y, p.Position.Z + vib.Z));
                DrawMesh(_unitBox);
            }
        }
    }

    private void DrawMachineNodesMainPass(
        Dictionary<string, EquipmentItem> eqMap,
        IReadOnlyDictionary<string, ProcessFlowState> flowMap,
        LoaderPose loaderPose,
        Unity3DEffectSettings fx,
        string? activeSelected,
        string? hoveredId)
    {
        foreach (var p in Plant3DSceneData.MachineNodes)
        {
            eqMap.TryGetValue(p.LinkedScadaId, out var eq);
            var flow = flowMap.GetValueOrDefault(p.Id);
            bool running = flow is not null ? flow.IsRunning : (eq is null || eq.Status != EquipmentStatus.Stopped);

            var tint = Plant3DSceneData.GetStatusTint(p, eq, fx);
            bool isSelected = string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase);
            bool isHovered = string.Equals(hoveredId, p.Id, StringComparison.OrdinalIgnoreCase);

            bool isStockpile = p.EquipmentType.Contains("Stockpile", StringComparison.OrdinalIgnoreCase);
            float tintMix = fx.ShowThermalHeatmap ? 0.78f
                : (eq?.Status is EquipmentStatus.Fault or EquipmentStatus.Warning or EquipmentStatus.Stopped ? (isStockpile ? 0.28f : 0.58f) : (isStockpile ? 0.20f : 0.08f));
            if (isSelected) { tint = Color.Parse("#38BDF8"); tintMix = MathF.Max(tintMix, 0.30f); }
            else if (isHovered) { tint = Color.Parse("#7DD3FC"); tintMix = MathF.Max(tintMix, 0.20f); }

            if (p.Id == "FEL-101")
            {
                RenderWheelLoader(loaderPose, tint, tintMix);
                continue;
            }

            var vib = (p.Vibrates && fx.EnableVibrationPhysics && running)
                ? Plant3DSceneData.ComputeScreenVibrationOffset(p.Id, SimTime, eq?.LoadPercent ?? 70.0)
                : Vector3.Zero;

            var model = Matrix4x4.CreateScale(p.Scale)
                      * Matrix4x4.CreateRotationY(p.YawDegrees * (MathF.PI / 180f))
                      * Matrix4x4.CreateTranslation(p.Position.X + vib.X, p.Position.Y + vib.Y, p.Position.Z + vib.Z);
            SetModelAndNormalMatrix(model);

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
                SetModelAndNormalMatrix(coneMat);
                _glUniform1f!(_uTintMix, 0.85f);
                DrawMesh(_unitCone);
            }
            else if (_unitBox is not null)
            {
                var fbMat = Matrix4x4.CreateScale(p.HalfExtents.X * 2f, p.HalfExtents.Y * 2f, p.HalfExtents.Z * 2f)
                          * Matrix4x4.CreateTranslation(p.Position.X + vib.X, p.Position.Y + p.HalfExtents.Y + vib.Y, p.Position.Z + vib.Z);
                SetModelAndNormalMatrix(fbMat);
                _glUniform1f!(_uTintMix, 0.75f);
                DrawMesh(_unitBox);
            }
        }
    }

    private void RenderWheelLoader(LoaderPose pose, Color tint, float tintMix)
    {
        const float deg = MathF.PI / 180f;
        var chassisWorld = Matrix4x4.CreateRotationZ(pose.PitchDegrees * deg)
                         * Matrix4x4.CreateRotationY(pose.YawDegrees * deg)
                         * Matrix4x4.CreateTranslation(pose.ChassisPosition);

        SetModelAndNormalMatrix(chassisWorld);
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
            SetModelAndNormalMatrix(bodyMat);
            _glUniform1f!(_uTintMix, 0.85f);
            DrawMesh(_unitBox);
        }

        var glbBucketWorld = Matrix4x4.CreateRotationZ((pose.BoomAngleDegrees + pose.BucketDumpAngleDegrees * 0.45f) * deg)
                           * Matrix4x4.CreateTranslation(1.30f, 1.38f, 0f)
                           * chassisWorld;

        if (_glbGpuMeshes.TryGetValue("wheel_loader_bucket.glb", out var bucketMesh))
        {
            SetModelAndNormalMatrix(glbBucketWorld);
            _glUniform3f!(_uTint, tint.R / 255f, tint.G / 255f, tint.B / 255f);
            _glUniform1f!(_uTintMix, tintMix);
            DrawMesh(bucketMesh);
        }
        else if (_unitBox is not null)
        {
            var scoopMat = Matrix4x4.CreateScale(0.85f, 0.58f, 1.85f)
                         * Matrix4x4.CreateTranslation(0.35f, -0.10f, 0f)
                         * glbBucketWorld;
            SetModelAndNormalMatrix(scoopMat);
            _glUniform3f!(_uTint, 0.25f, 0.30f, 0.38f);
            _glUniform1f!(_uTintMix, 1.0f);
            DrawMesh(_unitBox);
        }
    }

    private void DrawInstancedBatch(GpuMesh mesh, List<InstanceItem> items, bool isShadowPass)
    {
        int count = items.Count;
        if (count == 0)
            return;

        if (_glDrawElementsInstanced is null || _glBufferSubData is null || mesh.InstancedVao == 0)
        {
            // Fallback per-instance draw if instancing extension isn't present
            int modelLoc = isShadowPass ? _uShadowModel : _uModel;
            int instLoc = isShadowPass ? _uShadowIsInstanced : _uIsInstanced;
            _glUniform1f!(instLoc, 0f);
            for (int i = 0; i < count; i++)
            {
                var it = items[i];
                if (isShadowPass)
                    SetMatrix(modelLoc, it.Model);
                else
                {
                    SetModelAndNormalMatrix(it.Model);
                    _glUniform3f!(_uTint, it.R, it.G, it.B);
                    _glUniform1f!(_uAlpha, it.Alpha);
                }
                DrawMesh(mesh);
            }
            return;
        }

        if (count > _instanceCapacity)
        {
            _instanceCapacity = Math.Max(count, _instanceCapacity * 2);
            _instanceScratch = new float[_instanceCapacity * InstanceFloats];
            _glBindBuffer!(GL_ARRAY_BUFFER, _instanceVbo);
            _glBufferData!(GL_ARRAY_BUFFER, (nint)(_instanceCapacity * InstanceFloats * sizeof(float)), null, GL_DYNAMIC_DRAW);
            _glBindBuffer!(GL_ARRAY_BUFFER, 0);
        }

        for (int i = 0; i < count; i++)
        {
            var it = items[i];
            var m = it.Model;
            int o = i * InstanceFloats;
            _instanceScratch[o + 0] = m.M11;  _instanceScratch[o + 1] = m.M12;  _instanceScratch[o + 2] = m.M13;  _instanceScratch[o + 3] = m.M14;
            _instanceScratch[o + 4] = m.M21;  _instanceScratch[o + 5] = m.M22;  _instanceScratch[o + 6] = m.M23;  _instanceScratch[o + 7] = m.M24;
            _instanceScratch[o + 8] = m.M31;  _instanceScratch[o + 9] = m.M32;  _instanceScratch[o + 10] = m.M33; _instanceScratch[o + 11] = m.M34;
            _instanceScratch[o + 12] = m.M41; _instanceScratch[o + 13] = m.M42; _instanceScratch[o + 14] = m.M43; _instanceScratch[o + 15] = m.M44;
            _instanceScratch[o + 16] = it.R;
            _instanceScratch[o + 17] = it.G;
            _instanceScratch[o + 18] = it.B;
            _instanceScratch[o + 19] = it.Alpha;
            _instanceScratch[o + 20] = it.Metallic;
            _instanceScratch[o + 21] = it.Roughness;
        }

        _glBindBuffer!(GL_ARRAY_BUFFER, _instanceVbo);
        fixed (float* pData = _instanceScratch)
        {
            _glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)(count * InstanceFloats * sizeof(float)), pData);
        }
        _glBindBuffer!(GL_ARRAY_BUFFER, 0);

        _glUniform1f!(isShadowPass ? _uShadowIsInstanced : _uIsInstanced, 1.0f);
        _glBindVertexArray!(mesh.InstancedVao);
        _glDrawElementsInstanced(GL_TRIANGLES, mesh.IndexCount, GL_UNSIGNED_INT, 0, count);
        _frameDrawCalls++;
        _glUniform1f!(isShadowPass ? _uShadowIsInstanced : _uIsInstanced, 0.0f);
    }

    // =========================================================================
    // Shaders
    // =========================================================================

    private int BuildShadowProgram(bool isEs)
    {
        string header = isEs ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";

        string vsSrc = header + @"
in vec3 aPos;
in vec4 aInstCol0;
in vec4 aInstCol1;
in vec4 aInstCol2;
in vec4 aInstCol3;

uniform mat4 uLightViewProj;
uniform mat4 uModel;
uniform float uIsInstanced;

void main() {
    mat4 m = (uIsInstanced > 0.5)
        ? mat4(aInstCol0, aInstCol1, aInstCol2, aInstCol3)
        : uModel;
    gl_Position = uLightViewProj * (m * vec4(aPos, 1.0));
}
";

        string fsSrc = header + @"
out vec4 FragColor;
void main() {
    FragColor = vec4(0.0, 0.0, 0.0, 1.0);
}
";
        return CompileAndLinkProgram(vsSrc, fsSrc, bindInstancedAttribs: true);
    }

    private int BuildMainPbrProgram(bool isEs)
    {
        string header = isEs ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";

        string vsSrc = header + @"
in vec3 aPos;
in vec3 aNormal;
in vec3 aColor;
in vec2 aPbr;
in vec4 aInstCol0;
in vec4 aInstCol1;
in vec4 aInstCol2;
in vec4 aInstCol3;
in vec4 aInstColorAlpha;
in vec2 aInstPbr;

uniform mat4 uModel;
uniform mat3 uNormalMat;
uniform mat4 uViewProj;
uniform mat4 uLightViewProj;
uniform float uIsInstanced;

out vec3 vWorldPos;
out vec3 vNormal;
out vec3 vColor;
out vec2 vPbr;
out vec4 vShadowCoord;
out vec4 vInstColorAlpha;
out vec2 vInstPbr;
out vec3 vLocalPos;

void main() {
    vec4 wp;
    if (uIsInstanced > 0.5) {
        mat4 instM = mat4(aInstCol0, aInstCol1, aInstCol2, aInstCol3);
        wp = instM * vec4(aPos, 1.0);
        mat3 m3 = mat3(instM);
        mat3 adjT = mat3(cross(m3[1], m3[2]), cross(m3[2], m3[0]), cross(m3[0], m3[1]));
        vec3 n = adjT * aNormal;
        vNormal = dot(n, n) > 1e-8 ? normalize(n) : vec3(0.0, 1.0, 0.0);
        vInstColorAlpha = aInstColorAlpha;
        vInstPbr = aInstPbr;
    } else {
        wp = uModel * vec4(aPos, 1.0);
        vec3 n = uNormalMat * aNormal;
        vNormal = dot(n, n) > 1e-8 ? normalize(n) : vec3(0.0, 1.0, 0.0);
        vInstColorAlpha = vec4(1.0);
        vInstPbr = aPbr;
    }
    vWorldPos = wp.xyz;
    vColor = aColor;
    vPbr = aPbr;
    vLocalPos = aPos;
    vShadowCoord = uLightViewProj * wp;
    gl_Position = uViewProj * wp;
}
";

        string fsSrc = header + @"
in vec3 vWorldPos;
in vec3 vNormal;
in vec3 vColor;
in vec2 vPbr;
in vec4 vShadowCoord;
in vec4 vInstColorAlpha;
in vec2 vInstPbr;
in vec3 vLocalPos;

uniform vec3 uCamPos;
uniform vec3 uTint;
uniform float uTintMix;
uniform float uAlpha;
uniform float uUnlit;
uniform float uIsInstanced;
uniform sampler2D uShadowMap;
uniform float uShadowsEnabled;
uniform float uEncodeHdr;
uniform float uApplyToneMap;
uniform float uEnableClearcoat;
uniform float uSimTime;

out vec4 FragColor;

float hash31(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float noise3(vec3 x) {
    vec3 i = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(hash31(i + vec3(0.0, 0.0, 0.0)), hash31(i + vec3(1.0, 0.0, 0.0)), f.x),
            mix(hash31(i + vec3(0.0, 1.0, 0.0)), hash31(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
        mix(mix(hash31(i + vec3(0.0, 0.0, 1.0)), hash31(i + vec3(1.0, 0.0, 1.0)), f.x),
            mix(hash31(i + vec3(0.0, 1.0, 1.0)), hash31(i + vec3(1.0, 1.0, 1.0)), f.x), f.y),
        f.z);
}

float fbm3(vec3 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 3; i++) {
        v += a * noise3(p);
        p = p * 2.03 + vec3(17.1, 31.7, 11.3);
        a *= 0.5;
    }
    return v;
}

float sdBox2D(vec2 p, vec2 c, vec2 h) {
    vec2 d = abs(p - c) - h;
    return length(max(d, vec2(0.0))) + min(max(d.x, d.y), 0.0);
}

vec3 sampleIndustrialEnv(vec3 dir, float rough, vec3 sunDir) {
    vec3 skyTop = vec3(0.18, 0.34, 0.62);
    vec3 horizon = vec3(0.42, 0.52, 0.66);
    vec3 groundBounce = vec3(0.14, 0.11, 0.09);
    vec3 baseSky = mix(groundBounce, mix(horizon, skyTop, smoothstep(0.0, 0.65, dir.y)), smoothstep(-0.28, 0.22, dir.y));
    float sunExp = mix(48.0, 4.0, rough * rough);
    vec3 sunLobe = vec3(1.15, 0.88, 0.58) * pow(max(dot(dir, sunDir), 0.0), sunExp) * mix(0.85, 0.25, rough);
    float strip = smoothstep(0.55, 0.92, abs(cos(dir.x * 5.0))) * smoothstep(0.25, 0.72, dir.y);
    vec3 skylights = vec3(0.62, 0.78, 0.96) * strip * mix(0.85, 0.18, rough);
    return baseSky + sunLobe + skylights;
}

vec3 evalCookTorrance(
    vec3 N, vec3 V, vec3 L, vec3 lightCol,
    vec3 albedo, float metallic, float roughness, vec3 F0, float clearcoatWeight,
    vec3 T, vec3 B, float aniso) {
    vec3 H = normalize(L + V);
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return vec3(0.0);
    float NdotV = max(dot(N, V), 0.001);
    float NdotH = max(dot(N, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);

    float a = roughness * roughness;
    float ax = max(a * (1.0 + aniso), 0.002);
    float ay = max(a * (1.0 - aniso), 0.002);
    float TdotH = dot(T, H);
    float BdotH = dot(B, H);
    float dAniso = (TdotH * TdotH) / (ax * ax) + (BdotH * BdotH) / (ay * ay) + NdotH * NdotH;
    float D = 1.0 / (3.14159265 * ax * ay * dAniso * dAniso + 1e-5);

    float k = roughness + 1.0;
    k = (k * k) / 8.0;
    float G = (NdotV / (NdotV * (1.0 - k) + k)) * (NdotL / (NdotL * (1.0 - k) + k));
    vec3 F = F0 + (1.0 - F0) * pow(clamp(1.0 - HdotV, 0.0, 1.0), 5.0);

    // Kulla-Conty multi-scatter energy compensation
    vec3 Favg = F0 + (vec3(1.0) - F0) / 21.0;
    vec3 Ems = vec3(1.0) / max(vec3(0.32), vec3(1.0) - Favg * (roughness * 0.58));

    vec3 spec = ((D * G * F) / (4.0 * NdotV * NdotL + 0.001)) * mix(vec3(1.0), Ems, metallic * 0.85 + 0.15);
    vec3 kD = (1.0 - F) * (1.0 - metallic);
    vec3 contrib = kD * albedo / 3.14159265 + spec;

    if (clearcoatWeight > 0.01) {
        float aCoat = 0.10 * 0.10;
        float aCoat2 = aCoat * aCoat;
        float denomC = NdotH * NdotH * (aCoat2 - 1.0) + 1.0;
        float Dc = aCoat2 / (3.14159265 * denomC * denomC + 1e-5);
        float Fc = 0.04 + 0.96 * pow(clamp(1.0 - HdotV, 0.0, 1.0), 5.0);
        float LdotH = max(dot(H, L), 0.05);
        float Gc = 0.25 / (LdotH * LdotH);
        float coatSpec = Dc * Gc * Fc * clearcoatWeight;
        contrib = contrib * (1.0 - Fc * clearcoatWeight * 0.65) + vec3(coatSpec);
    }

    return contrib * lightCol * NdotL;
}

vec3 ACESFilm(vec3 x) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec3 tintSrgb = (uIsInstanced > 0.5) ? vInstColorAlpha.rgb : uTint;
    vec3 tintLinear = pow(max(tintSrgb, vec3(0.0)), vec3(2.2));
    float alpha = (uIsInstanced > 0.5) ? vInstColorAlpha.a : uAlpha;
    float rawMetallic = (uIsInstanced > 0.5) ? vInstPbr.x : vPbr.x;
    bool isGround = (rawMetallic < -0.5);
    float metallic = isGround ? 0.03 : clamp(rawMetallic, 0.0, 1.0);
    float roughness = isGround ? 0.88 : clamp((uIsInstanced > 0.5) ? vInstPbr.y : vPbr.y, 0.06, 1.0);

    vec3 albedo = mix(vColor, tintLinear, uTintMix);

    // Precompute screen-space derivatives uniformly before non-uniform material branches
    vec3 dpdx = dFdx(vWorldPos);
    vec3 dpdy = dFdy(vWorldPos);
    vec2 coord2 = vWorldPos.xz / 2.0;
    vec2 fw2 = max(fwidth(coord2), vec2(1e-4));
    vec2 coord10 = vWorldPos.xz / 10.0;
    vec2 fw10 = max(fwidth(coord10), vec2(1e-4));

    vec3 hdrCol;
    if (uUnlit > 1.5) {
        float radial = pow(clamp(1.0 - length(vLocalPos) * 2.0, 0.0, 1.0), 1.75);
        bool isSpray = (roughness < 0.5);
        vec3 drift = isSpray ? vec3(0.0, uSimTime * 1.8, 0.0) : vec3(0.18 * uSimTime, -0.55 * uSimTime, 0.12 * uSimTime);
        float wisp = fbm3(vWorldPos * (isSpray ? 3.8 : 2.1) + drift);
        float density = radial * smoothstep(0.16, 0.88, wisp * 1.15);
        alpha *= density;
        if (alpha < 0.005) discard;
        vec3 L = normalize(vec3(0.48, 0.82, 0.34));
        vec3 V = normalize(uCamPos - vWorldPos);
        float forwardScatter = pow(max(dot(-V, L), 0.0), 4.0) * 0.45 + 0.55;
        hdrCol = albedo * (isSpray ? (0.95 + 0.55 * forwardScatter) : (0.70 + 0.40 * forwardScatter));
    } else if (uUnlit > 0.5) {
        float boost = (metallic > 0.95) ? 2.4 : (alpha > 0.5 ? 1.32 : 1.0);
        hdrCol = albedo * boost;
    } else {
        vec3 N = normalize(vNormal);

        bool isWater = (!isGround && N.y > 0.70 && metallic < 0.25 && roughness < 0.26 && albedo.b > albedo.r * 2.0 && albedo.b > 0.04);
        bool isRubberBelt = (!isGround && !isWater && abs(metallic - 0.14) < 0.025 && roughness > 0.84 && roughness < 0.90);
        bool isConcrete = (!isGround && !isWater && !isRubberBelt && metallic < 0.12 && roughness > 0.76 && (dot(albedo, vec3(0.333)) >= 0.055 || albedo.b > albedo.r * 1.8));
        bool isCoal = (!isGround && !isWater && !isRubberBelt && !isConcrete && dot(albedo, vec3(0.333)) < 0.045 && metallic < 0.25);
        bool isGranular = (!isGround && !isWater && !isRubberBelt && roughness > 0.65 && metallic < 0.25);
        bool isBrushed = (metallic > 0.50);

        float freq = isGround ? 2.8 : (isCoal ? 12.5 : (isGranular ? 7.5 : (isBrushed ? 18.0 : 10.0)));
        float bumpAmp = isGround ? 0.045 : (isCoal ? 0.18 : (isGranular ? 0.14 : (isBrushed ? 0.035 : 0.02)));
        vec3 samplePos = isBrushed ? vec3(vWorldPos.x * 2.2, vWorldPos.y * 0.8, vWorldPos.z * 2.2) : vWorldPos;
        float h = fbm3(samplePos * freq);
        float groundPadContact = 1.0;
        vec3 groundGridGlow = vec3(0.0);

        if (isGround) {
            float gravel = fbm3(vec3(vWorldPos.x * 0.45, 0.0, vWorldPos.z * 0.45));
            float fineGravel = fbm3(vec3(vWorldPos.x * 3.2, 0.0, vWorldPos.z * 3.2));
            albedo = mix(vec3(0.015, 0.019, 0.028), vec3(0.030, 0.036, 0.048), gravel * 0.65 + fineGravel * 0.35);
            roughness = clamp(0.84 + fineGravel * 0.12, 0.06, 1.0);

            vec2 g2 = abs(fract(coord2 - 0.5) - 0.5) / fw2;
            float line2 = 1.0 - min(min(g2.x, g2.y), 1.0);
            vec2 g10 = abs(fract(coord10 - 0.5) - 0.5) / fw10;
            float line10 = 1.0 - min(min(g10.x, g10.y), 1.0);
            float yardDist = length(vWorldPos.xz - vec2(-2.0, -0.5));
            float gridFade = 1.0 - smoothstep(26.0, 42.0, yardDist);

            float minPad = 99.0;
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(-13.8, 11.4), vec2(3.0, 7.9)));
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(-10.5, -0.8), vec2(5.1, 2.2)));
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(0.4, 0.6), vec2(2.8, 2.4)));
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(10.4, -5.2), vec2(3.1, 6.0)));
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(-5.0, -7.2), vec2(7.25, 3.1)));
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(-1.6, -13.9), vec2(5.8, 3.8)));
            minPad = min(minPad, sdBox2D(vWorldPos.xz, vec2(-13.8, 13.8), vec2(3.9, 6.5)));

            groundPadContact = mix(0.46, 1.0, smoothstep(0.0, 1.35, max(minPad, 0.0)));
            float padSuppress = smoothstep(-0.1, 0.45, minPad);
            float minorMask = line2 * 0.22 * gridFade * padSuppress;
            float majorMask = line10 * 0.50 * gridFade * padSuppress;
            vec3 gridCol = mix(vec3(0.04, 0.14, 0.24), vec3(0.08, 0.36, 0.56), line10);
            albedo = mix(albedo, gridCol, clamp(max(minorMask, majorMask), 0.0, 0.65));
            groundGridGlow = gridCol * (minorMask * 0.08 + majorMask * 0.22);
        } else if (isWater) {
            vec2 tkDelta = vWorldPos.xz - vec2(-1.2, -15.8);
            float tkR = length(tkDelta);
            if (tkR < 2.80) {
                float tkAngle = atan(tkDelta.y, tkDelta.x);
                float rakeWake = sin(tkAngle * 2.0 + uSimTime * 0.90) * 0.5 + 0.5;
                float radialWave = sin(tkR * 14.0 - uSimTime * 2.6 + rakeWake * 1.4);
                float crossWave = cos(vWorldPos.x * 7.5 + uSimTime * 1.9) * sin(vWorldPos.z * 7.5 - uSimTime * 1.7);
                vec2 radDir = tkDelta / max(tkR, 0.1);
                float waveSlope = radialWave * 0.11 + crossWave * 0.07;
                N = normalize(N + vec3(waveSlope * radDir.x, 0.0, waveSlope * radDir.y));
                albedo *= mix(0.88, 1.18, 0.5 + 0.5 * radialWave);

                float foamNoise = fbm3(vec3(vWorldPos.x * 4.5, uSimTime * 0.45, vWorldPos.z * 4.5));
                float ringMask = smoothstep(2.22, 2.34, tkR) * (1.0 - smoothstep(2.49, 2.58, tkR));
                float foam = ringMask * smoothstep(0.30, 0.72, foamNoise + 0.18 * sin(tkAngle * 6.0 - uSimTime * 1.4));
                albedo = mix(albedo, vec3(0.72, 0.84, 0.92), foam * 0.78);
                roughness = mix(roughness, 0.58, foam * 0.70);
            } else {
                float w1 = sin(vWorldPos.x * 9.5 + uSimTime * 2.6) * cos(vWorldPos.z * 9.5 - uSimTime * 2.3);
                float w2 = sin((vWorldPos.x - vWorldPos.z) * 6.5 + uSimTime * 3.1);
                N = normalize(N + vec3(w1 * 0.13, 0.0, w2 * 0.13));
                albedo *= mix(0.86, 1.18, 0.5 + 0.5 * w1);
            }
            bumpAmp = 0.0;
        } else {
            if (isRubberBelt) {
                float treadA = abs(fract((vWorldPos.x + vWorldPos.z) * 2.2 - uSimTime * 0.85) - 0.5) * 2.0;
                float treadB = abs(fract((vWorldPos.x - vWorldPos.z) * 2.2 - uSimTime * 0.85) - 0.5) * 2.0;
                float chevron = smoothstep(0.68, 0.92, max(treadA, treadB));
                float wearTrack = fbm3(vec3(vWorldPos.x * 3.5, 0.0, vWorldPos.z * 3.5));
                albedo *= mix(0.84, 1.14, chevron * 0.55 + wearTrack * 0.45);
                roughness = clamp(roughness - chevron * 0.06, 0.06, 1.0);
            } else if (isConcrete && abs(N.y) > 0.70) {
                vec2 jg = abs(fract(coord2 - 0.5) - 0.5) / fw2;
                float joint = 1.0 - min(min(jg.x, jg.y), 1.0);
                albedo *= mix(1.0, 0.54, joint * 0.75);
                h -= joint * 0.18;
            }
            if (isCoal) {
                albedo *= mix(0.65, 1.48, h);
            } else if (isGranular) {
                albedo *= mix(0.84, 1.16, h);
            }
        }

        float dhdx = dFdx(h);
        float dhdy = dFdy(h);
        if (bumpAmp > 0.001) {
            vec3 r1 = cross(dpdy, N);
            vec3 r2 = cross(N, dpdx);
            float det = dot(dpdx, r1);
            if (abs(det) > 1e-6) {
                vec3 surfGrad = sign(det) * (dhdx * r1 + dhdy * r2) / abs(det);
                float gLen = length(surfGrad);
                if (gLen > 2.5) surfGrad *= (2.5 / gLen);
                N = normalize(N - bumpAmp * surfGrad);
            }
        }

        vec3 V = normalize(uCamPos - vWorldPos);
        float NdotV = max(dot(N, V), 0.001);
        vec3 upRef = (abs(N.y) < 0.92) ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
        vec3 T = normalize(cross(upRef, N));
        vec3 B = cross(N, T);
        float aniso = isBrushed ? 0.42 : 0.0;

        vec3 F0 = mix(vec3(isCoal ? 0.058 : 0.04), albedo, metallic);
        float clearcoatWeight = (uEnableClearcoat > 0.5 && roughness < 0.44 && metallic < 0.25)
            ? clamp((0.46 - roughness) * 3.0, 0.25, 0.90)
            : 0.0;

        // Primary directional light + 16-tap Rotated Vogel-Disk Poisson PCSS soft shadows
        vec3 L = normalize(vec3(0.48, 0.82, 0.34));
        float shadow = 1.0;
        if (uShadowsEnabled > 0.5 && vShadowCoord.w > 0.001) {
            vec3 projCoords = vShadowCoord.xyz / vShadowCoord.w;
            vec3 suv = projCoords * 0.5 + 0.5;
            if (suv.x > 0.01 && suv.x < 0.99 && suv.y > 0.01 && suv.y < 0.99 && suv.z >= 0.0 && suv.z <= 1.0) {
                float bias = max(0.0022 * (1.0 - max(dot(N, L), 0.0)), 0.0006);
                float ign = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
                float rotAngle = ign * 6.2831853;
                vec2 texelSize = vec2(1.0 / 2048.0);
                float litSum = 0.0;
                const float spread = 2.6;
                for (int i = 0; i < 16; i++) {
                    float r = sqrt((float(i) + 0.5) / 16.0);
                    float theta = float(i) * 2.3999632 + rotAngle;
                    vec2 offset = vec2(cos(theta), sin(theta)) * (r * spread) * texelSize;
                    float pcfDepth = texture(uShadowMap, suv.xy + offset).r;
                    litSum += (suv.z - bias > pcfDepth) ? 0.0 : 1.0;
                }
                shadow = litSum / 16.0;
            }
        }

        vec3 direct = evalCookTorrance(N, V, L, vec3(2.45, 2.38, 2.25), albedo, metallic, roughness, F0, clearcoatWeight, T, B, aniso) * shadow;

        // Secondary cool/warm industrial fill light
        vec3 L2 = normalize(vec3(-0.55, 0.38, -0.62));
        vec3 fill = evalCookTorrance(N, V, L2, vec3(0.46, 0.62, 0.84), albedo, metallic, roughness, F0, clearcoatWeight * 0.5, T, B, aniso);

        // 4 analytical downward industrial bay spotlights
        vec3 spots[4];
        spots[0] = vec3(-13.8, 9.2, 7.5);
        spots[1] = vec3(-10.5, 6.8, -0.6);
        spots[2] = vec3(0.2, 7.8, 0.5);
        spots[3] = vec3(-0.8, 8.2, -7.8);
        vec3 baySpots = vec3(0.0);
        for (int s = 0; s < 4; s++) {
            vec3 toSpot = spots[s] - vWorldPos;
            float d2 = dot(toSpot, toSpot);
            float d = sqrt(max(d2, 1e-4));
            vec3 Ls = toSpot / d;
            float cone = smoothstep(0.45, 0.86, Ls.y);
            float atten = (1.0 / (1.0 + 0.045 * d2)) * smoothstep(16.5, 7.5, d);
            vec3 spotCol = ((s == 3) ? vec3(0.88, 1.02, 1.15) : vec3(1.15, 1.06, 0.92)) * (atten * cone * 1.45);
            if (atten * cone > 0.005) {
                baySpots += evalCookTorrance(N, V, Ls, spotCol, albedo, metallic, roughness, F0, clearcoatWeight * 0.5, T, B, aniso);
            }
        }

        // Procedural industrial IBL (sun lobe, skylight strips, warm ground bounce) + wet clearcoat reflection
        vec3 R = reflect(-V, N);
        vec3 envDiff = sampleIndustrialEnv(N, 0.88, L);
        vec3 rawEnvSpec = sampleIndustrialEnv(R, roughness, L);
        vec3 envSpec = mix(rawEnvSpec, envDiff, roughness * roughness * 0.65);

        vec3 F_ibl = F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - NdotV, 0.0, 1.0), 5.0);
        vec3 kD_ibl = (1.0 - F_ibl) * (1.0 - metallic);
        float specOcc = mix(1.0, 0.28, roughness);
        float contactAo = mix(0.68, 1.0, smoothstep(0.0, 0.55, vWorldPos.y)) * groundPadContact;

        vec3 ambient = (kD_ibl * albedo * envDiff * 0.56 + F_ibl * envSpec * specOcc * 0.84) * contactAo;
        if (clearcoatWeight > 0.01) {
            float Fc_ibl = 0.04 + 0.96 * pow(clamp(1.0 - NdotV, 0.0, 1.0), 5.0);
            ambient += rawEnvSpec * (Fc_ibl * clearcoatWeight * 0.85) * contactAo;
        }

        // Grazing Fresnel silhouette rim for non-ground machinery & steel
        vec3 rimGlow = vec3(0.0);
        if (!isGround) {
            float rim = pow(clamp(1.0 - NdotV, 0.0, 1.0), 3.5);
            vec3 rimCol = mix(vec3(0.22, 0.44, 0.72), vec3(0.56, 0.76, 0.96), metallic);
            rimGlow = rimCol * rim * (0.20 + 0.24 * max(N.y, 0.0)) * contactAo;
        }

        vec3 lit = ambient + direct + fill + baySpots + rimGlow + groundGridGlow;

        float dist = length(uCamPos - vWorldPos);
        float fog = clamp((dist - 32.0) / 56.0, 0.0, 0.36);
        hdrCol = mix(lit, vec3(0.008, 0.018, 0.038), fog);
    }

    if (uEncodeHdr > 0.5) {
        // Compress HDR [0, 2.5] perceptually into RGBA8 [0, 1] with smooth dark resolution
        vec3 encoded = sqrt(clamp(hdrCol * 0.4, 0.0, 1.0));
        FragColor = vec4(encoded, alpha);
    } else if (uApplyToneMap > 0.5) {
        vec3 mapped = ACESFilm(hdrCol * 1.06);
        vec3 srgb = pow(mapped, vec3(1.0 / 2.2));
        FragColor = vec4(srgb, alpha);
    } else {
        FragColor = vec4(hdrCol, alpha);
    }
}
";
        return CompileAndLinkProgram(vsSrc, fsSrc, bindInstancedAttribs: true);
    }

    private int BuildPostProgram(bool isEs)
    {
        string header = isEs ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";

        string vsSrc = header + @"
out vec2 vUv;
void main() {
    vec2 pos = vec2((gl_VertexID == 1) ? 3.0 : -1.0, (gl_VertexID == 2) ? 3.0 : -1.0);
    vUv = pos * 0.5 + 0.5;
    gl_Position = vec4(pos, 0.0, 1.0);
}
";

        string fsSrc = header + @"
in vec2 vUv;

uniform sampler2D uColorTex;
uniform sampler2D uDepthTex;
uniform vec2 uTexelSize;
uniform float uEnableAo;
uniform float uEnableCas;
uniform float uDecodeHdr;
uniform float uIsOrtho;
uniform float uNear;
uniform float uFar;
uniform mat4 uInvViewProj;

out vec4 FragColor;

vec3 decodeHdr(vec3 c) {
    return (uDecodeHdr > 0.5) ? (c * c) * 2.5 : c;
}

float linearizeDepth(float d) {
    if (uIsOrtho > 0.5) {
        return mix(uNear, uFar, d);
    }
    float zNdc = d * 2.0 - 1.0;
    return (2.0 * uNear * uFar) / max(uFar + uNear - zNdc * (uFar - uNear), 0.0001);
}

vec3 sampleDuskSky(vec2 uv) {
    vec3 rayDir;
    if (uIsOrtho > 0.5) {
        float elev = clamp((uv.y - 0.12) * 1.15, 0.0, 1.0);
        rayDir = normalize(vec3(0.48, elev * 0.85, 0.34));
    } else {
        vec2 ndc = uv * 2.0 - 1.0;
        vec4 pNear = uInvViewProj * vec4(ndc, -1.0, 1.0);
        vec4 pFar = uInvViewProj * vec4(ndc, 1.0, 1.0);
        vec3 wNear = pNear.xyz / max(abs(pNear.w), 1e-5);
        vec3 wFar = pFar.xyz / max(abs(pFar.w), 1e-5);
        rayDir = normalize(wFar - wNear);
    }

    vec3 fogColor = vec3(0.008, 0.018, 0.038);
    vec3 horizonGlow = vec3(0.026, 0.044, 0.078);
    vec3 zenithCol = vec3(0.004, 0.009, 0.024);

    float up = max(rayDir.y, 0.0);
    vec3 grad = mix(fogColor, mix(horizonGlow, zenithCol, smoothstep(0.04, 0.58, up)), smoothstep(-0.02, 0.06, rayDir.y));
    if (rayDir.y < 0.0) {
        grad = mix(fogColor, vec3(0.004, 0.010, 0.022), clamp(-rayDir.y * 2.4, 0.0, 1.0));
    }

    vec3 L = normalize(vec3(0.48, 0.82, 0.34));
    float sunCos = max(dot(rayDir, L), 0.0);
    float scatter = pow(sunCos, 10.0) * 0.14 + pow(sunCos, 96.0) * 0.42;
    return grad + vec3(1.0, 0.72, 0.42) * scatter;
}

vec3 ACESFilm(vec3 x) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec3 hdrCol = decodeHdr(texture(uColorTex, vUv).rgb);
    float centerDepth = texture(uDepthTex, vUv).r;
    float linCenter = linearizeDepth(centerDepth);
    float maxSlope = 0.25 + 0.04 * linCenter;
    float dzdx = clamp(dFdx(linCenter), -maxSlope, maxSlope);
    float dzdy = clamp(dFdy(linCenter), -maxSlope, maxSlope);

    if (centerDepth >= 0.9995) {
        hdrCol = sampleDuskSky(vUv);
    }

    // 12-tap Normal-Aware Depth-Plane Bilateral GTAO-Lite
    float ao = 1.0;
    if (uEnableAo > 0.5 && centerDepth < 0.9995) {
        float ign = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
        float ang = ign * 6.2831853;
        float ca = cos(ang);
        float sa = sin(ang);
        mat2 rot = mat2(ca, -sa, sa, ca);

        float radiusPx = uIsOrtho > 0.5 ? 7.5 : clamp(96.0 / max(linCenter, 4.0), 3.5, 14.0);
        float occSum = 0.0;
        for (int i = 0; i < 12; i++) {
            float r = sqrt((float(i) + 0.5) / 12.0);
            float theta = float(i) * 2.3999632;
            vec2 tapVec = rot * (vec2(cos(theta), sin(theta)) * r);
            vec2 pxDelta = tapVec * radiusPx;
            vec2 uvTap = vUv + pxDelta * uTexelSize;
            float tapDepth = texture(uDepthTex, uvTap).r;
            if (tapDepth >= 0.9995) continue;
            float linTap = linearizeDepth(tapDepth);
            float planeLin = linCenter + dzdx * pxDelta.x + dzdy * pxDelta.y;
            float diff = planeLin - linTap;
            float horizon = clamp(diff / (0.42 + 0.02 * linCenter), 0.0, 1.0);
            float bilateralWeight = 1.0 - smoothstep(0.75, 1.95, abs(diff));
            occSum += smoothstep(0.05, 0.30, diff) * horizon * bilateralWeight;
        }
        ao = clamp(1.0 - (occSum / 12.0) * 0.72, 0.38, 1.0);
    }

    // Subtle highlight bloom for bright emissive / specular pixels (luminance > 1.05)
    vec3 bloom = vec3(0.0);
    vec2 bOff[8];
    bOff[0] = vec2(-2.0, -2.0); bOff[1] = vec2( 2.0, -2.0);
    bOff[2] = vec2(-2.0,  2.0); bOff[3] = vec2( 2.0,  2.0);
    bOff[4] = vec2(-4.5,  0.0); bOff[5] = vec2( 4.5,  0.0);
    bOff[6] = vec2( 0.0, -4.5); bOff[7] = vec2( 0.0,  4.5);
    for (int i = 0; i < 8; i++) {
        vec3 nCol = decodeHdr(texture(uColorTex, vUv + bOff[i] * uTexelSize).rgb);
        float lum = dot(nCol, vec3(0.2126, 0.7152, 0.0722));
        if (lum > 1.05) {
            bloom += nCol * ((lum - 1.05) / max(lum, 1e-4));
        }
    }
    bloom *= (1.0 / 8.0) * 0.24;

    vec3 finalHdr = (hdrCol * ao + bloom) * 1.06;
    vec3 mapped = ACESFilm(finalHdr);

    // Depth-aware FidelityFX Contrast Adaptive Sharpening (CAS)
    if (uEnableCas > 0.5 && centerDepth < 0.9995) {
        float dN = linearizeDepth(texture(uDepthTex, vUv + vec2(0.0, uTexelSize.y)).r);
        float dS = linearizeDepth(texture(uDepthTex, vUv - vec2(0.0, uTexelSize.y)).r);
        float dE = linearizeDepth(texture(uDepthTex, vUv + vec2(uTexelSize.x, 0.0)).r);
        float dW = linearizeDepth(texture(uDepthTex, vUv - vec2(uTexelSize.x, 0.0)).r);
        float maxEdgeDiff = max(max(abs(dN - linCenter), abs(dS - linCenter)), max(abs(dE - linCenter), abs(dW - linCenter)));
        float edgeMask = 1.0 - smoothstep(0.35, 1.60, maxEdgeDiff);

        vec3 nCol = ACESFilm((decodeHdr(texture(uColorTex, vUv + vec2(0.0, uTexelSize.y)).rgb) * ao + bloom) * 1.06);
        vec3 sCol = ACESFilm((decodeHdr(texture(uColorTex, vUv - vec2(0.0, uTexelSize.y)).rgb) * ao + bloom) * 1.06);
        vec3 eCol = ACESFilm((decodeHdr(texture(uColorTex, vUv + vec2(uTexelSize.x, 0.0)).rgb) * ao + bloom) * 1.06);
        vec3 wCol = ACESFilm((decodeHdr(texture(uColorTex, vUv - vec2(uTexelSize.x, 0.0)).rgb) * ao + bloom) * 1.06);
        vec3 mn = min(mapped, min(min(nCol, sCol), min(eCol, wCol)));
        vec3 mx = max(mapped, max(max(nCol, sCol), max(eCol, wCol)));
        vec3 amp = clamp(min(mn, vec3(1.0) - mx) / max(mx, vec3(1e-4)), 0.0, 1.0);
        vec3 weight = sqrt(amp) * (-0.16 * edgeMask);
        mapped = clamp((mapped + (nCol + sCol + eCol + wCol) * weight) / (vec3(1.0) + 4.0 * weight), 0.0, 1.0);
    }

    vec2 vCent = vUv - vec2(0.5);
    float vig = clamp(1.0 - dot(vCent, vCent) * 0.24, 0.78, 1.0);
    mapped *= vig;

    vec3 srgb = pow(mapped, vec3(1.0 / 2.2));
    FragColor = vec4(srgb, 1.0);
}
";
        return CompileAndLinkProgram(vsSrc, fsSrc, bindInstancedAttribs: false);
    }

    private int CompileAndLinkProgram(string vsSrc, string fsSrc, bool bindInstancedAttribs)
    {
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
        if (bindInstancedAttribs)
        {
            BindAttribLocation(prog, 4, "aInstCol0");
            BindAttribLocation(prog, 5, "aInstCol1");
            BindAttribLocation(prog, 6, "aInstCol2");
            BindAttribLocation(prog, 7, "aInstCol3");
            BindAttribLocation(prog, 8, "aInstColorAlpha");
            BindAttribLocation(prog, 9, "aInstPbr");
        }
        _glLinkProgram!(prog);

        int ok = 0;
        _glGetProgramiv!(prog, GL_LINK_STATUS, &ok);
        _glDeleteShader!(vs);
        _glDeleteShader!(fs);
        if (ok == 0)
        {
            if (_glGetProgramInfoLog is not null)
            {
                var logBuf = stackalloc byte[1024];
                int len = 0;
                _glGetProgramInfoLog(prog, 1024, &len, logBuf);
                var msg = Encoding.UTF8.GetString(logBuf, Math.Clamp(len, 0, 1023));
                Console.Error.WriteLine($"[Plant3D] Program link error: {msg}");
            }
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
            if (_glGetShaderInfoLog is not null)
            {
                var logBuf = stackalloc byte[1024];
                int len = 0;
                _glGetShaderInfoLog(shader, 1024, &len, logBuf);
                var msg = Encoding.UTF8.GetString(logBuf, Math.Clamp(len, 0, 1023));
                Console.Error.WriteLine($"[Plant3D] Shader compile error ({type}): {msg}");
            }
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

    private int GetUniformLoc(int program, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return _glGetUniformLocation!(program, p);
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

    private void SetModelAndNormalMatrix(Matrix4x4 model)
    {
        SetMatrix(_uModel, model);
        if (_uNormalMat < 0 || _glUniformMatrix3fv is null)
            return;

        var nMat = Matrix4x4.Identity;
        if (Matrix4x4.Invert(model, out var inv))
        {
            nMat = Matrix4x4.Transpose(inv);
        }

        float* ptr = stackalloc float[9];
        ptr[0] = nMat.M11; ptr[1] = nMat.M12; ptr[2] = nMat.M13;
        ptr[3] = nMat.M21; ptr[4] = nMat.M22; ptr[5] = nMat.M23;
        ptr[6] = nMat.M31; ptr[7] = nMat.M32; ptr[8] = nMat.M33;
        _glUniformMatrix3fv(_uNormalMat, 1, 0, ptr);
    }

    private GpuMesh UploadIndexedMesh(float[] vertices, uint[] indices, bool withInstancing)
    {
        uint vao = 0, instVao = 0, vbo = 0, ebo = 0;
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

        if (withInstancing && _instanceVbo != 0 && _glVertexAttribDivisor is not null)
        {
            _glGenVertexArrays!(1, &instVao);
            _glBindVertexArray!(instVao);

            _glBindBuffer!(GL_ARRAY_BUFFER, vbo);
            _glBindBuffer!(GL_ELEMENT_ARRAY_BUFFER, ebo);
            _glEnableVertexAttribArray!(0);
            _glVertexAttribPointer!(0, 3, GL_FLOAT, 0, stride, 0);
            _glEnableVertexAttribArray!(1);
            _glVertexAttribPointer!(1, 3, GL_FLOAT, 0, stride, 3 * sizeof(float));
            _glEnableVertexAttribArray!(2);
            _glVertexAttribPointer!(2, 3, GL_FLOAT, 0, stride, 6 * sizeof(float));
            _glEnableVertexAttribArray!(3);
            _glVertexAttribPointer!(3, 2, GL_FLOAT, 0, stride, 9 * sizeof(float));

            _glBindBuffer!(GL_ARRAY_BUFFER, _instanceVbo);
            int instStride = InstanceFloats * sizeof(float);
            for (uint loc = 4; loc <= 7; loc++)
            {
                _glEnableVertexAttribArray!(loc);
                _glVertexAttribPointer!(loc, 4, GL_FLOAT, 0, instStride, (nint)((loc - 4) * 4 * sizeof(float)));
                _glVertexAttribDivisor(loc, 1);
            }
            _glEnableVertexAttribArray!(8);
            _glVertexAttribPointer!(8, 4, GL_FLOAT, 0, instStride, 16 * sizeof(float));
            _glVertexAttribDivisor(8, 1);

            _glEnableVertexAttribArray!(9);
            _glVertexAttribPointer!(9, 2, GL_FLOAT, 0, instStride, 20 * sizeof(float));
            _glVertexAttribDivisor(9, 1);
        }

        _glBindVertexArray!(0);
        _glBindBuffer!(GL_ARRAY_BUFFER, 0);
        return new GpuMesh { Vao = vao, InstancedVao = instVao, Vbo = vbo, Ebo = ebo, IndexCount = indices.Length };
    }

    private GpuMesh UploadGlbMesh(string fileName, GlbMeshData meshData)
    {
        bool isTk201 = string.Equals(fileName, "thickener_tk201.glb", StringComparison.OrdinalIgnoreCase);
        bool isTk301 = string.Equals(fileName, "magnetite_tank_tk301.glb", StringComparison.OrdinalIgnoreCase);
        if (!isTk201 && !isTk301)
            return UploadIndexedMesh(meshData.Vertices, meshData.Indices, withInstancing: false);

        var verts = meshData.Vertices;
        var srcIdx = meshData.Indices;
        var kept = new List<uint>(srcIdx.Length);
        const int stride = GlbMeshData.FloatsPerVertex;

        for (int i = 0; i + 2 < srcIdx.Length; i += 3)
        {
            uint i0 = srcIdx[i], i1 = srcIdx[i + 1], i2 = srcIdx[i + 2];
            int o0 = (int)i0 * stride, o1 = (int)i1 * stride, o2 = (int)i2 * stride;
            float cx = (verts[o0] + verts[o1] + verts[o2]) / 3f;
            float cy = (verts[o0 + 1] + verts[o1 + 1] + verts[o2 + 1]) / 3f;
            float cz = (verts[o0 + 2] + verts[o1 + 2] + verts[o2 + 2]) / 3f;
            float ny = (verts[o0 + 4] + verts[o1 + 4] + verts[o2 + 4]) / 3f;
            float r = MathF.Sqrt(cx * cx + cz * cz);

            if (isTk201)
            {
                bool isLaunderCap = r < 2.96f && MathF.Abs(ny) > 0.92f && (MathF.Abs(cy - 1.69f) < 0.015f || MathF.Abs(cy - 1.47f) < 0.015f);
                bool isBasinLid = r < 2.82f && ny > 0.92f && MathF.Abs(cy - 1.60f) < 0.015f;
                bool isOpaqueSlurry = r < 2.70f && cy > 1.42f && cy < 1.54f && verts[o0 + 9] < 0.25f && verts[o0 + 10] < 0.26f;
                if (isLaunderCap || isBasinLid || isOpaqueSlurry) continue;
            }
            else if (isTk301)
            {
                bool isRimCap = r < 1.52f && MathF.Abs(ny) > 0.92f && (MathF.Abs(cy - 2.44f) < 0.012f || MathF.Abs(cy - 2.32f) < 0.012f);
                bool isShellLid = r < 1.44f && ny > 0.92f && MathF.Abs(cy - 2.375f) < 0.012f;
                if (isRimCap || isShellLid) continue;
            }

            kept.Add(i0);
            kept.Add(i1);
            kept.Add(i2);
        }

        return UploadIndexedMesh(verts, kept.ToArray(), withInstancing: false);
    }

    private void DrawMesh(GpuMesh m)
    {
        _glBindVertexArray!(m.Vao);
        _glDrawElements!(GL_TRIANGLES, m.IndexCount, GL_UNSIGNED_INT, 0);
        _frameDrawCalls++;
    }

    private void DeleteMesh(GpuMesh? m)
    {
        if (m is null) return;
        uint vao = m.Vao, instVao = m.InstancedVao, vbo = m.Vbo, ebo = m.Ebo;
        if (vao != 0) _glDeleteVertexArrays?.Invoke(1, &vao);
        if (instVao != 0) _glDeleteVertexArrays?.Invoke(1, &instVao);
        if (vbo != 0) _glDeleteBuffers?.Invoke(1, &vbo);
        if (ebo != 0) _glDeleteBuffers?.Invoke(1, &ebo);
    }

    // =========================================================================
    // Procedural Mesh Builders (Vertex colors stored in linear space)
    // =========================================================================

    private static float SrgbToLinear(float c) => MathF.Pow(Math.Clamp(c, 0f, 1f), 2.2f);

    private static void AddVert(List<float> v, Vector3 p, Vector3 n, float r = 1f, float g = 1f, float b = 1f, float met = 0.4f, float rough = 0.5f)
    {
        v.Add(p.X); v.Add(p.Y); v.Add(p.Z);
        v.Add(n.X); v.Add(n.Y); v.Add(n.Z);
        v.Add(r); v.Add(g); v.Add(b);
        v.Add(met); v.Add(rough);
    }

    private static void AppendBox(List<float> v, List<uint> idx, Matrix4x4 xform, float r, float g, float b, float met = 0.6f, float rough = 0.45f, bool convertSrgbToLinear = false)
    {
        float lr = convertSrgbToLinear ? SrgbToLinear(r) : r;
        float lg = convertSrgbToLinear ? SrgbToLinear(g) : g;
        float lb = convertSrgbToLinear ? SrgbToLinear(b) : b;

        void Quad(Vector3 a, Vector3 b0, Vector3 c, Vector3 d, Vector3 n)
        {
            var wa = Vector3.Transform(a, xform);
            var wb = Vector3.Transform(b0, xform);
            var wc = Vector3.Transform(c, xform);
            var wd = Vector3.Transform(d, xform);
            var wn = Vector3.Normalize(Vector3.TransformNormal(n, xform));
            uint s = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, wa, wn, lr, lg, lb, met, rough);
            AddVert(v, wb, wn, lr, lg, lb, met, rough);
            AddVert(v, wc, wn, lr, lg, lb, met, rough);
            AddVert(v, wd, wn, lr, lg, lb, met, rough);
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
        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: true);
    }

    private GpuMesh BuildCoalLumpMesh()
    {
        // Perturbed irregular 20-face icosahedron with flat per-face normals for realistic coal cleavage facets
        const float phi = 1.61803399f;
        var raw = new Vector3[]
        {
            new(-1f,  phi, 0f), new( 1f,  phi, 0f), new(-1f, -phi, 0f), new( 1f, -phi, 0f),
            new( 0f, -1f,  phi), new( 0f,  1f,  phi), new( 0f, -1f, -phi), new( 0f,  1f, -phi),
            new( phi, 0f, -1f), new( phi, 0f,  1f), new(-phi, 0f, -1f), new(-phi, 0f,  1f)
        };
        float[] jitter = { 0.88f, 1.12f, 0.94f, 1.08f, 0.82f, 1.16f, 0.91f, 1.06f, 0.85f, 1.14f, 0.96f, 1.03f };
        var verts = new Vector3[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            verts[i] = Vector3.Normalize(raw[i]) * (0.50f * jitter[i]);

        int[,] tris =
        {
            {0, 11, 5}, {0, 5, 1}, {0, 1, 7}, {0, 7, 10}, {0, 10, 11},
            {1, 5, 9}, {5, 11, 4}, {11, 10, 2}, {10, 7, 6}, {7, 1, 8},
            {3, 9, 4}, {3, 4, 2}, {3, 2, 6}, {3, 6, 8}, {3, 8, 9},
            {4, 9, 5}, {2, 4, 11}, {6, 2, 10}, {8, 6, 7}, {9, 8, 1}
        };

        var v = new List<float>(60 * GlbMeshData.FloatsPerVertex);
        var idx = new List<uint>(60);
        for (int t = 0; t < 20; t++)
        {
            var p0 = verts[tris[t, 0]];
            var p1 = verts[tris[t, 1]];
            var p2 = verts[tris[t, 2]];
            var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            if (Vector3.Dot(n, p0 + p1 + p2) < 0f)
                n = -n;
            uint b = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, p0, n, 1f, 1f, 1f, 0.06f, 0.82f);
            AddVert(v, p1, n, 1f, 1f, 1f, 0.06f, 0.82f);
            AddVert(v, p2, n, 1f, 1f, 1f, 0.06f, 0.82f);
            idx.AddRange(new uint[] { b, b + 1, b + 2 });
        }

        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: true);
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
            AddVert(v, new(c0, 0.5f, s0), new(c0, 0, s0));
            AddVert(v, new(c1, 0.5f, s1), new(c1, 0, s1));
            AddVert(v, new(c1, -0.5f, s1), new(c1, 0, s1));
            idx.AddRange(new uint[] { b, b + 1, b + 2, b, b + 2, b + 3 });

            uint t = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(0, 0.5f, 0), Vector3.UnitY);
            AddVert(v, new(c1, 0.5f, s1), Vector3.UnitY);
            AddVert(v, new(c0, 0.5f, s0), Vector3.UnitY);
            idx.AddRange(new uint[] { t, t + 1, t + 2 });

            uint bot = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, new(0, -0.5f, 0), -Vector3.UnitY);
            AddVert(v, new(c0, -0.5f, s0), -Vector3.UnitY);
            AddVert(v, new(c1, -0.5f, s1), -Vector3.UnitY);
            idx.AddRange(new uint[] { bot, bot + 1, bot + 2 });
        }
        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: true);
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
        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: true);
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
        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: true);
    }

    private GpuMesh BuildGroundTerrainQuad()
    {
        var v = new List<float>(4 * GlbMeshData.FloatsPerVertex);
        const float y = -0.02f;
        const float cx = -2.0f, cz = -0.5f, half = 44.0f;
        AddVert(v, new(cx - half, y, cz - half), Vector3.UnitY, 0.02f, 0.025f, 0.035f, -1.0f, 0.88f);
        AddVert(v, new(cx + half, y, cz - half), Vector3.UnitY, 0.02f, 0.025f, 0.035f, -1.0f, 0.88f);
        AddVert(v, new(cx + half, y, cz + half), Vector3.UnitY, 0.02f, 0.025f, 0.035f, -1.0f, 0.88f);
        AddVert(v, new(cx - half, y, cz + half), Vector3.UnitY, 0.02f, 0.025f, 0.035f, -1.0f, 0.88f);
        var idx = new uint[] { 0, 2, 1, 0, 3, 2 };
        return UploadIndexedMesh(v.ToArray(), idx, withInstancing: false);
    }

    private GpuMesh BuildOrthogonalPipesMesh()
    {
        var v = new List<float>();
        var idx = new List<uint>();
        const int sides = 14;
        float flR = SrgbToLinear(0.22f), flG = SrgbToLinear(0.27f), flB = SrgbToLinear(0.34f);

        foreach (var route in Plant3DSceneData.PipeRoutes)
        {
            var col = Plant3DSceneData.GetMediumColor(route.Medium);
            float cr = SrgbToLinear(0.50f + (col.R / 255f) * 0.35f);
            float cg = SrgbToLinear(0.56f + (col.G / 255f) * 0.35f);
            float cb = SrgbToLinear(0.64f + (col.B / 255f) * 0.35f);
            float flangeR = route.Radius * 1.52f;

            for (int w = 0; w < route.Waypoints.Length - 1; w++)
            {
                var a = route.Waypoints[w];
                var b = route.Waypoints[w + 1];
                AppendSegmentTube(v, idx, a, b, route.Radius, sides, cr, cg, cb);

                var delta = b - a;
                float segLen = delta.Length();
                if (segLen > 0.55f)
                {
                    var dir = delta / segLen;
                    float off = MathF.Min(route.Radius * 1.85f, segLen * 0.22f);
                    var fA = a + dir * off;
                    var fB = b - dir * off;
                    AppendSegmentTube(v, idx, fA - dir * 0.03f, fA + dir * 0.03f, flangeR, sides, flR, flG, flB, 0.82f, 0.36f, capEnds: true);
                    AppendSegmentTube(v, idx, fB - dir * 0.03f, fB + dir * 0.03f, flangeR, sides, flR, flG, flB, 0.82f, 0.36f, capEnds: true);
                }
            }

            for (int w = 1; w < route.Waypoints.Length - 1; w++)
            {
                AppendSphere(v, idx, route.Waypoints[w], route.Radius * 1.12f, 6, 10, cr, cg, cb, 0.72f, 0.32f);
            }
        }

        // Inline valves with bolted flanges and red (isolation) / yellow (modulating/medium) spoked handwheels
        AppendInlineValve(v, idx, new(-3.85f, 0.56f, -15.00f), Vector3.UnitX, 0.075f, isRedHandwheel: true);
        AppendInlineValve(v, idx, new(-3.85f, 0.56f, -16.30f), Vector3.UnitX, 0.075f, isRedHandwheel: true);
        AppendInlineValve(v, idx, new(0.65f, 2.10f, -15.80f), Vector3.UnitX, 0.045f, isRedHandwheel: false);
        AppendInlineValve(v, idx, new(7.10f, 0.95f, -11.50f), Vector3.UnitX, 0.065f, isRedHandwheel: true);
        AppendInlineValve(v, idx, new(0.15f, 1.25f, -6.55f), Vector3.UnitZ, 0.070f, isRedHandwheel: false);
        AppendInlineValve(v, idx, new(0.15f, 1.25f, -8.75f), Vector3.UnitZ, 0.070f, isRedHandwheel: false);
        AppendInlineValve(v, idx, new(2.45f, 1.15f, -9.60f), Vector3.UnitZ, 0.075f, isRedHandwheel: false);
        AppendInlineValve(v, idx, new(-0.65f, 3.08f, -9.28f), Vector3.UnitX, 0.065f, isRedHandwheel: true);

        // PT-301 pressure transmitter manifold & gauge on PIPE-304 riser
        AppendPressureTransmitter(v, idx, new(2.30f, 2.95f, -8.30f), Vector3.UnitY);

        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: false);
    }

    private static void AppendSegmentTube(
        List<float> v, List<uint> idx, Vector3 a, Vector3 b, float radius, int sides,
        float cr, float cg, float cb, float met = 0.72f, float rough = 0.32f, bool capEnds = false)
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
            AddVert(v, a + n0 * radius, n0, cr, cg, cb, met, rough);
            AddVert(v, a + n1 * radius, n1, cr, cg, cb, met, rough);
            AddVert(v, b + n1 * radius, n1, cr, cg, cb, met, rough);
            AddVert(v, b + n0 * radius, n0, cr, cg, cb, met, rough);
            idx.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2, baseIdx, baseIdx + 2, baseIdx + 3 });

            if (capEnds)
            {
                uint cA = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
                AddVert(v, a, -dir, cr, cg, cb, met, rough);
                AddVert(v, a + n1 * radius, -dir, cr, cg, cb, met, rough);
                AddVert(v, a + n0 * radius, -dir, cr, cg, cb, met, rough);
                idx.AddRange(new uint[] { cA, cA + 1, cA + 2 });

                uint cB = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
                AddVert(v, b, dir, cr, cg, cb, met, rough);
                AddVert(v, b + n0 * radius, dir, cr, cg, cb, met, rough);
                AddVert(v, b + n1 * radius, dir, cr, cg, cb, met, rough);
                idx.AddRange(new uint[] { cB, cB + 1, cB + 2 });
            }
        }
    }

    private static void AppendSphere(
        List<float> v, List<uint> idx, Vector3 center, float radius, int stacks, int slices,
        float cr, float cg, float cb, float met, float rough)
    {
        uint start = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
        for (int st = 0; st <= stacks; st++)
        {
            float phi = MathF.PI * st / stacks;
            float sp = MathF.Sin(phi), cp = MathF.Cos(phi);
            for (int sl = 0; sl <= slices; sl++)
            {
                float th = 2f * MathF.PI * sl / slices;
                var n = new Vector3(sp * MathF.Cos(th), cp, sp * MathF.Sin(th));
                AddVert(v, center + n * radius, n, cr, cg, cb, met, rough);
            }
        }

        int stride = slices + 1;
        for (int st = 0; st < stacks; st++)
        {
            for (int sl = 0; sl < slices; sl++)
            {
                uint a = start + (uint)(st * stride + sl);
                uint b = a + (uint)stride;
                idx.AddRange(new uint[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        }
    }

    private static void AppendInlineValve(List<float> v, List<uint> idx, Vector3 pos, Vector3 axis, float pipeRadius, bool isRedHandwheel)
    {
        var dir = Vector3.Normalize(axis);
        float bodyR = pipeRadius * 1.65f;
        float flangeR = pipeRadius * 1.95f;
        float br = SrgbToLinear(0.18f), bg = SrgbToLinear(0.24f), bb = SrgbToLinear(0.32f);
        float chr = SrgbToLinear(0.72f), chg = SrgbToLinear(0.76f), chb = SrgbToLinear(0.82f);

        AppendSegmentTube(v, idx, pos - dir * 0.16f, pos + dir * 0.16f, bodyR, 12, br, bg, bb, 0.78f, 0.36f, capEnds: true);
        AppendSegmentTube(v, idx, pos - dir * 0.17f, pos - dir * 0.12f, flangeR, 12, br, bg, bb, 0.82f, 0.34f, capEnds: true);
        AppendSegmentTube(v, idx, pos + dir * 0.12f, pos + dir * 0.17f, flangeR, 12, br, bg, bb, 0.82f, 0.34f, capEnds: true);

        var stemDir = MathF.Abs(dir.Y) > 0.85f ? Vector3.UnitX : Vector3.UnitY;
        var stemTop = pos + stemDir * (bodyR + 0.18f);
        AppendSegmentTube(v, idx, pos + stemDir * (bodyR * 0.6f), stemTop, 0.030f, 10, chr, chg, chb, 0.90f, 0.22f, capEnds: true);

        var hwR = isRedHandwheel ? SrgbToLinear(0.92f) : SrgbToLinear(0.95f);
        var hwG = isRedHandwheel ? SrgbToLinear(0.16f) : SrgbToLinear(0.74f);
        var hwB = isRedHandwheel ? SrgbToLinear(0.14f) : SrgbToLinear(0.08f);

        var u = Vector3.Normalize(Vector3.Cross(stemDir, MathF.Abs(Vector3.Dot(stemDir, Vector3.UnitZ)) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var w = Vector3.Normalize(Vector3.Cross(stemDir, u));
        const float wheelR = 0.15f;
        const int wheelSegs = 10;
        for (int s = 0; s < wheelSegs; s++)
        {
            float a0 = s * MathF.PI * 2f / wheelSegs;
            float a1 = (s + 1) * MathF.PI * 2f / wheelSegs;
            var p0 = stemTop + (u * MathF.Cos(a0) + w * MathF.Sin(a0)) * wheelR;
            var p1 = stemTop + (u * MathF.Cos(a1) + w * MathF.Sin(a1)) * wheelR;
            AppendSegmentTube(v, idx, p0, p1, 0.022f, 8, hwR, hwG, hwB, 0.35f, 0.38f, capEnds: true);
        }
        AppendSegmentTube(v, idx, stemTop - u * wheelR, stemTop + u * wheelR, 0.016f, 6, hwR, hwG, hwB, 0.35f, 0.38f);
        AppendSegmentTube(v, idx, stemTop - w * wheelR, stemTop + w * wheelR, 0.016f, 6, hwR, hwG, hwB, 0.35f, 0.38f);
    }

    private static void AppendPressureTransmitter(List<float> v, List<uint> idx, Vector3 pos, Vector3 pipeAxis)
    {
        var dir = Vector3.Normalize(pipeAxis);
        float dkR = SrgbToLinear(0.20f), dkG = SrgbToLinear(0.25f), dkB = SrgbToLinear(0.32f);
        float chr = SrgbToLinear(0.76f), chg = SrgbToLinear(0.80f), chb = SrgbToLinear(0.86f);
        AppendSegmentTube(v, idx, pos - dir * 0.10f, pos + dir * 0.10f, 0.14f, 12, dkR, dkG, dkB, 0.82f, 0.34f, capEnds: true);
        var outDir = Vector3.UnitX;
        var gaugePos = pos + outDir * 0.24f;
        AppendSegmentTube(v, idx, pos, gaugePos, 0.028f, 8, chr, chg, chb, 0.90f, 0.20f, capEnds: true);
        AppendSegmentTube(v, idx, gaugePos - Vector3.UnitZ * 0.035f, gaugePos + Vector3.UnitZ * 0.035f, 0.095f, 12, chr, chg, chb, 0.88f, 0.22f, capEnds: true);
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

            // 1. Matte rubber belt bed (metallic = 0.14f, roughness = 0.87f triggers scrolling chevron/wear tracks)
            var beltMat = Matrix4x4.CreateScale(len, 0.08f, c.Width * 0.90f) * beltRot * Matrix4x4.CreateTranslation(mid);
            AppendBox(v, idx, beltMat, 0.25f, 0.29f, 0.35f, 0.14f, 0.87f, convertSrgbToLinear: true);

            // 2. Left & right steel side stringer channels
            foreach (float side in new[] { -halfW, halfW })
            {
                var sidePos = mid + Vector3.Transform(new Vector3(0f, -0.04f, side), beltRot);
                var strMat = Matrix4x4.CreateScale(len + 0.35f, 0.22f, 0.08f) * beltRot * Matrix4x4.CreateTranslation(sidePos);
                AppendBox(v, idx, strMat, 0.30f, 0.37f, 0.45f, 0.75f, 0.38f, convertSrgbToLinear: true);
            }

            // 3. Cross-bracing struts & 35-deg troughing wing-idler brackets under belt every ~1.8m
            int crossCount = Math.Max(2, (int)(len / 1.8f));
            for (int i = 0; i <= crossCount; i++)
            {
                var p = Vector3.Lerp(c.TailPos, c.HeadPos, (float)i / crossCount);
                var crossMat = Matrix4x4.CreateScale(0.08f, 0.10f, c.Width + 0.06f) * beltRot * Matrix4x4.CreateTranslation(p.X, p.Y - 0.12f, p.Z);
                AppendBox(v, idx, crossMat, 0.26f, 0.32f, 0.40f, 0.72f, 0.42f, convertSrgbToLinear: true);

                if (i > 0 && i < crossCount)
                {
                    var centerIdler = Matrix4x4.CreateScale(0.06f, 0.06f, c.Width * 0.42f)
                                    * beltRot
                                    * Matrix4x4.CreateTranslation(p + Vector3.Transform(new Vector3(0f, -0.055f, 0f), beltRot));
                    AppendBox(v, idx, centerIdler, 0.56f, 0.62f, 0.70f, 0.80f, 0.32f, convertSrgbToLinear: true);

                    foreach (float wingSide in new[] { -1f, 1f })
                    {
                        var wingOff = Vector3.Transform(new Vector3(0f, 0.01f, wingSide * c.Width * 0.34f), beltRot);
                        var wingMat = Matrix4x4.CreateScale(0.065f, 0.065f, c.Width * 0.28f)
                                    * Matrix4x4.CreateRotationX(-wingSide * 0.610865f)
                                    * beltRot
                                    * Matrix4x4.CreateTranslation(p + wingOff);
                        AppendBox(v, idx, wingMat, 0.88f, 0.58f, 0.10f, 0.62f, 0.38f, convertSrgbToLinear: true);
                    }
                }
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
                    AppendBox(v, idx, legMat, 0.25f, 0.31f, 0.39f, 0.75f, 0.42f, convertSrgbToLinear: true);
                }

                if (h > 1.2f)
                {
                    var braceMat = Matrix4x4.CreateScale(0.08f, 0.08f, c.Width + 0.18f)
                                 * flatRot
                                 * Matrix4x4.CreateTranslation(bp.X, h * 0.52f, bp.Z);
                    AppendBox(v, idx, braceMat, 0.28f, 0.34f, 0.42f, 0.75f, 0.42f, convertSrgbToLinear: true);
                }
            }

            // 5. Side maintenance walkway + safety-yellow handrail, mid-rail (y=0.42) & kickplate (y=0.06)
            if (len > 4.2f || MathF.Abs(c.HeadPos.Y - c.TailPos.Y) > 0.6f)
            {
                float walkZ = halfW + 0.34f;
                var walkMid = mid + Vector3.Transform(new Vector3(0f, -0.05f, walkZ), beltRot);
                var walkMat = Matrix4x4.CreateScale(len * 0.92f, 0.05f, 0.52f) * beltRot * Matrix4x4.CreateTranslation(walkMid);
                AppendBox(v, idx, walkMat, 0.20f, 0.24f, 0.30f, 0.62f, 0.48f, convertSrgbToLinear: true);

                float railZ = halfW + 0.58f;
                var railMid = mid + Vector3.Transform(new Vector3(0f, 0.78f, railZ), beltRot);
                var railMat = Matrix4x4.CreateScale(len * 0.92f, 0.05f, 0.05f) * beltRot * Matrix4x4.CreateTranslation(railMid);
                AppendBox(v, idx, railMat, 0.95f, 0.76f, 0.08f, 0.35f, 0.45f, convertSrgbToLinear: true);

                var midRailPos = mid + Vector3.Transform(new Vector3(0f, 0.42f, railZ), beltRot);
                var midRailMat = Matrix4x4.CreateScale(len * 0.92f, 0.04f, 0.04f) * beltRot * Matrix4x4.CreateTranslation(midRailPos);
                AppendBox(v, idx, midRailMat, 0.95f, 0.76f, 0.08f, 0.35f, 0.45f, convertSrgbToLinear: true);

                var kickPos = mid + Vector3.Transform(new Vector3(0f, 0.06f, railZ), beltRot);
                var kickMat = Matrix4x4.CreateScale(len * 0.92f, 0.11f, 0.03f) * beltRot * Matrix4x4.CreateTranslation(kickPos);
                AppendBox(v, idx, kickMat, 0.95f, 0.76f, 0.08f, 0.35f, 0.45f, convertSrgbToLinear: true);

                int posts = Math.Max(3, (int)(len / 2.0f));
                for (int p = 0; p <= posts; p++)
                {
                    float pf = 0.04f + 0.92f * p / posts;
                    var pp = Vector3.Lerp(c.TailPos, c.HeadPos, pf) + Vector3.Transform(new Vector3(0f, 0.38f, railZ), beltRot);
                    var postMat = Matrix4x4.CreateScale(0.045f, 0.80f, 0.045f) * flatRot * Matrix4x4.CreateTranslation(pp);
                    AppendBox(v, idx, postMat, 0.95f, 0.76f, 0.08f, 0.35f, 0.45f, convertSrgbToLinear: true);
                }
            }

            // 6. Receiving Skirtboard Hopper at TailPos
            foreach (float side in new[] { -halfW + 0.05f, halfW - 0.05f })
            {
                var skPos = c.TailPos + Vector3.Transform(new Vector3(0.45f, 0.22f, side), beltRot);
                var skMat = Matrix4x4.CreateScale(1.1f, 0.36f, 0.07f) * beltRot * Matrix4x4.CreateTranslation(skPos);
                AppendBox(v, idx, skMat, 0.36f, 0.44f, 0.52f, 0.68f, 0.42f, convertSrgbToLinear: true);
            }

            // 7. Head Drive Motor & Gearbox block beside HeadPos
            var motorPos = c.HeadPos + Vector3.Transform(new Vector3(-0.25f, -0.05f, -halfW - 0.32f), flatRot);
            var motorMat = Matrix4x4.CreateScale(0.65f, 0.42f, 0.44f) * flatRot * Matrix4x4.CreateTranslation(motorPos);
            AppendBox(v, idx, motorMat, 0.12f, 0.35f, 0.58f, 0.68f, 0.36f, convertSrgbToLinear: true);

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
            AppendBox(v, idx, chuteMat, 0.24f, 0.30f, 0.38f, 0.72f, 0.45f, convertSrgbToLinear: true);

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
                    AppendBox(v, idx, sMat, 0.32f, 0.40f, 0.48f, 0.70f, 0.42f, convertSrgbToLinear: true);
                }
            }
        }

        AppendPlantDetailsToStructureMesh(v, idx);
        return UploadIndexedMesh(v.ToArray(), idx.ToArray(), withInstancing: false);
    }

    private static void AppendAnnularRim(
        List<float> v, List<uint> idx, Vector3 center,
        float rInner, float rOuter, float yBot, float yTop, int segs,
        float r, float g, float b, float met, float rough)
    {
        float lr = SrgbToLinear(r), lg = SrgbToLinear(g), lb = SrgbToLinear(b);
        for (int i = 0; i < segs; i++)
        {
            float a0 = i * MathF.PI * 2f / segs;
            float a1 = (i + 1) * MathF.PI * 2f / segs;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
            float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);

            // Inner weir wall (facing inward)
            uint iw = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, center + new Vector3(c0 * rInner, yBot, s0 * rInner), new(-c0, 0, -s0), lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c1 * rInner, yBot, s1 * rInner), new(-c1, 0, -s1), lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c1 * rInner, yTop, s1 * rInner), new(-c1, 0, -s1), lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c0 * rInner, yTop, s0 * rInner), new(-c0, 0, -s0), lr, lg, lb, met, rough);
            idx.AddRange(new uint[] { iw, iw + 1, iw + 2, iw, iw + 2, iw + 3 });

            // Top rim annulus
            uint tr = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, center + new Vector3(c0 * rInner, yTop, s0 * rInner), Vector3.UnitY, lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c1 * rInner, yTop, s1 * rInner), Vector3.UnitY, lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c1 * rOuter, yTop, s1 * rOuter), Vector3.UnitY, lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c0 * rOuter, yTop, s0 * rOuter), Vector3.UnitY, lr, lg, lb, met, rough);
            idx.AddRange(new uint[] { tr, tr + 1, tr + 2, tr, tr + 2, tr + 3 });

            // Bottom rim annulus
            uint br = (uint)(v.Count / GlbMeshData.FloatsPerVertex);
            AddVert(v, center + new Vector3(c0 * rOuter, yBot, s0 * rOuter), -Vector3.UnitY, lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c1 * rOuter, yBot, s1 * rOuter), -Vector3.UnitY, lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c1 * rInner, yBot, s1 * rInner), -Vector3.UnitY, lr, lg, lb, met, rough);
            AddVert(v, center + new Vector3(c0 * rInner, yBot, s0 * rInner), -Vector3.UnitY, lr, lg, lb, met, rough);
            idx.AddRange(new uint[] { br, br + 1, br + 2, br, br + 2, br + 3 });
        }
    }

    private static void AppendPlantDetailsToStructureMesh(List<float> v, List<uint> idx)
    {
        // Open-top launder rim for TK-201 and top rim collar for TK-301
        AppendAnnularRim(v, idx, new(-1.20f, 0f, -15.80f), 2.52f, 2.95f, 1.47f, 1.69f, 32, 0.16f, 0.20f, 0.26f, 0.82f, 0.38f);
        AppendAnnularRim(v, idx, new(-5.40f, 0f, -12.00f), 1.28f, 1.50f, 2.32f, 2.44f, 28, 0.16f, 0.20f, 0.26f, 0.82f, 0.38f);

        // GATE-101 (BN-101 discharge throat isolation gate + actuator) & CHUTE-100 (BN-101 -> VF-101 transfer chute)
        AppendBox(v, idx, Matrix4x4.CreateScale(1.06f, 0.18f, 1.06f) * Matrix4x4.CreateTranslation(-13.80f, 4.84f, 8.80f),
            0.90f, 0.62f, 0.10f, 0.55f, 0.40f, convertSrgbToLinear: true);
        AppendSegmentTube(v, idx, new(-13.26f, 4.84f, 8.80f), new(-12.75f, 4.84f, 8.80f), 0.055f, 10,
            SrgbToLinear(0.75f), SrgbToLinear(0.78f), SrgbToLinear(0.84f), 0.90f, 0.22f, capEnds: true);
        AppendBox(v, idx, Matrix4x4.CreateScale(1.18f, 0.66f, 1.32f) * Matrix4x4.CreateTranslation(-13.80f, 5.26f, 7.65f),
            0.28f, 0.34f, 0.42f, 0.72f, 0.44f, convertSrgbToLinear: true);

        // PAN-101 Eastward undersize chute extension (VF-101 -> CV-102)
        var panDir = Vector3.Normalize(new Vector3(0.72f, -0.22f, -0.04f));
        float panYaw = MathF.Atan2(-panDir.Z, panDir.X);
        float panPitch = MathF.Asin(panDir.Y);
        AppendBox(v, idx,
            Matrix4x4.CreateScale(0.86f, 0.34f, 0.86f)
            * Matrix4x4.CreateRotationZ(panPitch)
            * Matrix4x4.CreateRotationY(panYaw)
            * Matrix4x4.CreateTranslation(-13.10f, 4.66f, 6.12f),
            0.30f, 0.37f, 0.45f, 0.75f, 0.42f, convertSrgbToLinear: true);

        // DIV-101 motorized flop-gate diverter box (CV-103 / DRC-101 / CV-104)
        AppendBox(v, idx, Matrix4x4.CreateScale(0.92f, 0.86f, 0.92f) * Matrix4x4.CreateTranslation(-8.15f, 2.30f, -0.55f),
            0.26f, 0.33f, 0.41f, 0.76f, 0.40f, convertSrgbToLinear: true);
        AppendBox(v, idx, Matrix4x4.CreateScale(0.32f, 0.38f, 0.24f) * Matrix4x4.CreateTranslation(-8.15f, 2.35f, -1.10f),
            0.94f, 0.70f, 0.10f, 0.45f, 0.40f, convertSrgbToLinear: true);

        // BW-101 / BW-102 / BW-201 belt weighers mounted cleanly along host conveyors
        void AppendBeltWeigher(string convId, float t)
        {
            var conv = Plant3DSceneData.FindConveyor(convId);
            if (conv is null) return;
            var d = conv.HeadPos - conv.TailPos;
            float l = d.Length();
            if (l < 0.1f) return;
            var dn = d / l;
            float yaw = MathF.Atan2(-dn.Z, dn.X);
            float pitch = MathF.Asin(Math.Clamp(dn.Y, -1f, 1f));
            var rot = Matrix4x4.CreateRotationZ(pitch) * Matrix4x4.CreateRotationY(yaw);
            var pos = Vector3.Lerp(conv.TailPos, conv.HeadPos, t);

            float bwHalf = conv.Width * 0.56f;
            foreach (float side in new[] { -bwHalf, bwHalf })
            {
                var sidePt = pos + Vector3.Transform(new Vector3(0f, 0.26f, side), rot);
                AppendBox(v, idx, Matrix4x4.CreateScale(0.42f, 0.52f, 0.08f) * rot * Matrix4x4.CreateTranslation(sidePt),
                    0.94f, 0.72f, 0.08f, 0.45f, 0.40f, convertSrgbToLinear: true);
            }
            var archPt = pos + Vector3.Transform(new Vector3(0f, 0.52f, 0f), rot);
            AppendBox(v, idx, Matrix4x4.CreateScale(0.24f, 0.10f, conv.Width * 1.24f) * rot * Matrix4x4.CreateTranslation(archPt),
                0.94f, 0.72f, 0.08f, 0.45f, 0.40f, convertSrgbToLinear: true);
        }
        AppendBeltWeigher("CV-101", 0.50f);
        AppendBeltWeigher("CV-102", 0.25f);
        AppendBeltWeigher("CV-201", 0.50f);

        // RC-101 / RC-102 emergency pull-wire rip-cord switches + red safety cable along CV-101 & CV-102
        void AppendRipCord(string convId, float sideSign)
        {
            var conv = Plant3DSceneData.FindConveyor(convId);
            if (conv is null) return;
            var d = conv.HeadPos - conv.TailPos;
            float l = d.Length();
            if (l < 0.5f) return;
            var dn = d / l;
            float yaw = MathF.Atan2(-dn.Z, dn.X);
            float pitch = MathF.Asin(Math.Clamp(dn.Y, -1f, 1f));
            var rot = Matrix4x4.CreateRotationZ(pitch) * Matrix4x4.CreateRotationY(yaw);
            var sideOff = Vector3.Transform(new Vector3(0f, 0.26f, sideSign * (conv.Width * 0.5f + 0.14f)), rot);
            var pA = Vector3.Lerp(conv.TailPos, conv.HeadPos, 0.15f) + sideOff;
            var pB = Vector3.Lerp(conv.TailPos, conv.HeadPos, 0.85f) + sideOff;
            AppendSegmentTube(v, idx, pA, pB, 0.018f, 6, SrgbToLinear(0.92f), SrgbToLinear(0.16f), SrgbToLinear(0.14f), 0.25f, 0.45f, capEnds: true);
            var switchPos = Vector3.Lerp(pA, pB, 0.5f);
            AppendBox(v, idx, Matrix4x4.CreateScale(0.20f, 0.22f, 0.14f) * rot * Matrix4x4.CreateTranslation(switchPos),
                0.92f, 0.16f, 0.14f, 0.45f, 0.38f, convertSrgbToLinear: true);
        }
        AppendRipCord("CV-101", -1f);
        AppendRipCord("CV-102", 1f);

        // LAUNDER-201 peripheral overflow box & East weir takeoff on TK-201
        AppendBox(v, idx, Matrix4x4.CreateScale(0.76f, 0.42f, 0.76f) * Matrix4x4.CreateTranslation(1.35f, 1.48f, -14.40f),
            0.18f, 0.38f, 0.58f, 0.76f, 0.36f, convertSrgbToLinear: true);
        AppendBox(v, idx, Matrix4x4.CreateScale(0.62f, 0.36f, 0.88f) * Matrix4x4.CreateTranslation(1.75f, 1.54f, -15.80f),
            0.22f, 0.28f, 0.36f, 0.78f, 0.38f, convertSrgbToLinear: true);

        // Instrumentation sensor housings: VT-102 (SS-101), LT-201 (TK-201 bridge), DT-301 (PIPE-303)
        AppendBox(v, idx, Matrix4x4.CreateScale(0.24f, 0.26f, 0.18f) * Matrix4x4.CreateTranslation(0.25f, 2.40f, 1.38f),
            0.22f, 0.28f, 0.36f, 0.80f, 0.34f, convertSrgbToLinear: true);
        AppendBox(v, idx, Matrix4x4.CreateScale(0.22f, 0.30f, 0.22f) * Matrix4x4.CreateTranslation(-0.55f, 2.05f, -15.25f),
            0.22f, 0.28f, 0.36f, 0.80f, 0.34f, convertSrgbToLinear: true);
        AppendSegmentTube(v, idx, new(-2.68f, 1.45f, -10.20f), new(-2.32f, 1.45f, -10.20f), 0.16f, 12,
            SrgbToLinear(0.14f), SrgbToLinear(0.65f), SrgbToLinear(0.62f), 0.78f, 0.32f, capEnds: true);
    }
}
