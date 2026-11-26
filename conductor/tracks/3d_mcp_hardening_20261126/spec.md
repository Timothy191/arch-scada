# Specification: 3D MCP Hardening, Runtime Loader Safeguards & Web3D Bug Fixes (TODO 1-6)

## Overview
Resolve all 10 high-risk failure modes identified across the 4 configured free 3D MCP servers (`blender-mcp`, `3d-asset-processing-mcp`, `openusd-mcp`, `shader-polyhaven-mcp`) and the `ArchScada` 3D runtime viewports (`DigitalTwin3D.cshtml`, `Plant3DOpenGlCanvas.cs`, `GlbMeshLoader.cs`, and `verify-quality-gates.sh`).

## Functional Requirements
1. **TODO-01 (`DigitalTwin3D.cshtml`):** Fix live JS syntax error at lines 2844–2852 (`Unexpected token 'else'`) and fix `THREE.LOD` double-offset positioning/parenting bug at lines 2540–2582.
2. **TODO-02 (`3d-asset-processing-mcp`):** Patch `validate_model` (`validatorResult.issues?.messages`), isolate multi-step `gltf-transform-executor` intermediate `*_stepN_*.glb` files in `os.tmpdir()` with guaranteed `try/finally` cleanup, and provide a `SharpGLTF`-safe optimization tool.
3. **TODO-03 (`openusd-mcp`):** Persist `usd_set_variant` changes to disk via `stage.GetRootLayer().Save()` and apply `ComputeLocalToWorldTransform` during `usd_export_mesh`.
4. **TODO-04 (`blender-mcp`):** Disable outbound Supabase startup telemetry in `blender_mcp/config.py`, add headless `--background` offscreen render fallback to `get_viewport_screenshot` in `blender_mcp.py`, and version-control the headless daemon at `deploy/scripts/start-blender-mcp-headless.sh`.
5. **TODO-05 (`shader-polyhaven-mcp`):** Promote server to version-controlled `deploy/scripts/shader_polyhaven_mcp.py` (with `.agents/mcp/shader_polyhaven_mcp.py` symlink), add automated extraction and `glslangValidator` compilation of all embedded C# GLSL shaders in `Plant3DOpenGlCanvas.cs` across both `#version 150 core` and `#version 300 es`, and enforce `SharpGLTF` extension compatibility checks.
6. **TODO-06 (`verify-quality-gates.sh`):** Wire GLB extension compatibility, embedded C# GLSL shader validation (`--self-test`), and `DigitalTwin3D.cshtml` `node --check` JS syntax verification into `verify-quality-gates.sh`, and pass all 6 gates.
