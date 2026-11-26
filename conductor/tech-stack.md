# Technology Stack: Arch SCADA

## Core Application Stack
- **Runtime & SDK:** .NET 10.0 (`C# 13`)
- **Desktop HMI:** Avalonia UI + Custom OpenGL 3.2 Core / OpenGL ES 3.0 3-Pass HDR PBR Engine (`Plant3DOpenGlCanvas.cs`)
- **3D Asset Loading (Desktop):** `SharpGLTF.Core` + `StbImageSharp` (`GlbMeshLoader.cs`)
- **Web Portal & Web3D:** ASP.NET Core Razor Pages (`ScadaWeb`) + Three.js (`GLTFLoader`, `EffectComposer`, `UnrealBloomPass`, `N8AOPass`, `THREE.LOD`)
- **Database & Historian:** PostgreSQL 16 + SQLite local cache
- **Testing & Verification:** xUnit + Moq + 6-Gate `verify-quality-gates.sh`

## 3D Model Context Protocol (MCP) Toolchain (`.mcp.json`)
- **`blender-mcp`:** `mcp-for-blender` v2.1.1 + Blender 5.2.2 LTS Addon v1.7 (Protocol 11) + Headless TCP daemon (`localhost:9876`)
- **`3d-asset-processing-mcp`:** v0.0.1-beta.6 + `@gltf-transform/cli` v4.5.1 + `gltf-pipeline` v4.3.1 + `gltf-validator`
- **`openusd-mcp`:** v0.1.0 + Pixar `usd-core` (`pxr`) v26.11 + `mcp` v1.30.0
- **`shader-polyhaven-mcp`:** FastMCP + Khronos `/usr/bin/glslangValidator` + CC0 Poly Haven REST API + GLB/Shader Auditor
