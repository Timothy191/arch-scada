# Project Memory & Historical Decision Ledger (`conductor/memory.md`)

> **Last Updated:** 2026-11-26  
> **Scope:** Arch SCADA v6.5.3+ (CHPP 3D Digital Twin, 4-Server Free 3D MCP Toolchain, Quality Gates & Specialist Workflows)

---

## 1. Chronological Decision & Implementation Ledger

### M-01: 4-Server Zero-Subscription 3D MCP Stack Selection (`.mcp.json`)
* **Context:** Required an AI-driven 3D content authoring, optimization, OpenUSD digital twin, and shader verification pipeline without paid commercial subscriptions (replacing paid Meshy/Tripo cloud credit plans).
* **Decision:** Deployed four 100% free, local `stdio` MCP servers exposing **54 verified tools**:
  1. **`blender-mcp`** (`mcp-for-blender v2.1.1`, Addon `v1.7`, Protocol `11`, 36 tools): Connected to local `Blender 5.2.2 LTS` over TCP `localhost:9876`, with built-in **CC0 Poly Haven** integration and local self-hosted Hunyuan3D (`http://localhost:8081`) support.
  2. **`3d-asset-processing-mcp`** (`v0.0.1-beta.6`, `@gltf-transform/cli v4.5.1`, `gltf-pipeline v4.3.1`, 4 tools): Automated glTF/GLB geometry analysis, spec validation, and multi-step optimization.
  3. **`openusd-mcp`** (`v0.1.0`, Pixar `usd-core v26.11`, `mcp` pinned to `v1.30.0`, 8 tools): OpenUSD (`.usd`, `.usda`, `.usdc`, `.usdz`) hierarchy, material, variant, and world-space STL/OBJ mesh exporter.
  4. **`shader-polyhaven-mcp`** (`deploy/scripts/shader_polyhaven_mcp.py`, 6 tools): Khronos `/usr/bin/glslangValidator` shader compiler, embedded C# OpenGL (`#version 150`) & GLES 3.0 (`#version 300 es`) shader extractor, direct CC0 Poly Haven REST bridge, `SharpGLTF` extension auditor, and safe GLB optimizer.

### M-02: Cross-Runtime GLB Compatibility Constraint (`SharpGLTF` vs. `Three.js`)
* **Discovery:** `ArchScada.Desktop` loads `ArchScada/Assets/3D/Exported/*.glb` via `GlbMeshLoader.cs` (`SharpGLTF.Schema2.ModelRoot.ParseGLB` + `StbImageSharp`), which bakes textures into per-vertex colors (`FloatsPerVertex = 11`) and **cannot decode** `KHR_draco_mesh_compression`, `EXT_meshopt_compression`, `KHR_texture_basisu` (KTX2), or `EXT_texture_webp`. Meanwhile, `ScadaWeb` (`DigitalTwin3D.cshtml`) links those exact same 24 `.glb` files into `wwwroot/models3d/*.glb` via `ScadaWeb.csproj`.
* **Operational Rule:** Never apply Draco, Meshopt, KTX2, or WebP compression in-place to `ArchScada/Assets/3D/Exported/*.glb`. Always use `safe_optimize_archscada_glb` (`weld`, `dedup`, `prune`, `reorder`), which is enforced automatically by `deploy/scripts/shader_polyhaven_mcp.py --self-test` in Gate 3 of `verify-quality-gates.sh`.

### M-03: Critical 3D Runtime & Shader Bugs Resolved
1. **`DigitalTwin3D.cshtml` (`ScadaWeb`):**
   * Fixed syntax error at line 2848 (`Unexpected token 'else'`).
   * Fixed `THREE.LOD` double-offset coordinate bug (20m box and 50m sphere LODs now use local coordinates `(0, m.half[1], 0)` inside `entry.root`).
   * Removed non-existent `new THREE.LODManager()` and `new THREE.WebGLShadowMap()` calls that previously crashed the 3D scene into the 2D `startOfflineFallback()` canvas.
   * Hoisted `let bloomPass = null;` to module scope, removed a duplicate `#camera-bar` click listener, and upgraded `clearcoat`-bearing materials to `THREE.MeshPhysicalMaterial`.
2. **`Plant3DOpenGlCanvas.cs` (`ArchScada.Desktop`):**
   * Renamed custom 3D value-noise helper `float noise3(vec3)` to `float vnoise3(vec3)` to resolve a GLSL `#version 150` built-in `vec3 noise3(genType)` return-type overload conflict caught by `glslangValidator`.
3. **`task_start.sh` & `deliver_precontext.sh`:**
   * Fixed `PROJECT_ROOT` resolution from `"$(cd "$(dirname "$0")/.." && pwd)"` (which pointed one directory above the repo) to `"$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"`.

### M-04: Third-Party MCP Server Patches Applied
1. **`3d-asset-processing-mcp`:**
   * Patched `validator-simple.js` to iterate `validatorResult.issues?.messages || []`.
   * Patched `gltf-transform-executor.js` to support all 35 `gltf-transform` operations in `planExecutionSteps()` and isolate all `*_stepN_*.glb` intermediate files in `os.tmpdir()` with `try / finally` cleanup so Gate 3 (`exported_names == desktop_refs`) is never polluted.
2. **`openusd-mcp`:**
   * Pinned `mcp==1.30.0` (`mcp<2.0.0`) in its `uv` tool virtualenv.
   * Patched `tools.py` `set_variant()` to persist changes via `stage.GetRootLayer().Save()` and `export_mesh()` to apply `ComputeLocalToWorldTransform()`.
3. **`blender-mcp`:**
   * Disabled outbound Supabase startup telemetry (`enabled: bool = False` in `blender_mcp/config.py`).
   * Added headless `--background` `BLENDER_WORKBENCH` offscreen still rendering fallback to `get_viewport_screenshot` in `~/.config/blender/5.2/scripts/addons/blender_mcp.py` and created `deploy/scripts/start-blender-mcp-headless.sh`.
