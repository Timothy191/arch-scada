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


### M-05 (2026-11-26): Swarm Onboarding & Multi-Agent Work Distribution
* **Context:** The ArchSCADA 3D Digital Twin & MCP Swarm started from `master @ bf7eb86c2` (`ACT-01`: hardened 3D MCP toolchain, Web3D LOD/GLSL fixes, Conductor track) with strict file ownership per agent to avoid edit collisions.
* **Decision:** Work split into disjoint slices coordinated on the shared message board:
  | Agent | Actions | Owned files |
  | :--- | :--- | :--- |
  | `web-pipeline` | `ACT-02` Gate 5 headless-browser WebGL render assertion; `ACT-04` dual-tier Meshopt GLB pipeline | `deploy/scripts/optimize-web-glbs.sh`, `ArchScada/Assets/3D/ExportedWeb/*.glb`, `ScadaWeb.csproj`, `verify-quality-gates.sh` |
  | `kinematics-3d` | `ACT-03` `--verify-deployment` GLB check `16/16` -> `24/24`; `ACT-05` animated sub-equipment kinematics + `MeshoptDecoder` wiring | `Plant3DOpenGlCanvas.cs`, `DigitalTwin3D.cshtml`, `DeploymentReadinessService.cs` |
  | `openusd-memory` | `ACT-06` master OpenUSD assembly + dated memories | `deploy/scripts/generate_openusd_plant.py`, `ArchScada/Assets/3D/ArchCoalPlant.usda`, `conductor/memory.md` |
* **Rule:** Cross-slice needs (e.g. a 2-line `MeshoptDecoder` import in `DigitalTwin3D.cshtml`) are requested on the board by name, never by editing another agent's file.

### M-06 (2026-11-26): Dual-Tier GLB Asset Architecture (`SharpGLTF` Desktop vs. Meshopt Web3D)
* **Decision:** Two parallel GLB tiers sharing identical filenames and geometry (24 models each):
  1. **Desktop master tier** - `ArchScada/Assets/3D/Exported/*.glb` (~5.46 MiB, uncompressed, `weld/dedup/prune/reorder` only). Loaded by `GlbMeshLoader.cs` via `SharpGLTF`, which cannot decode Meshopt/Draco/KTX2/WebP (see `M-02`).
  2. **Web3D tier** - `ArchScada/Assets/3D/ExportedWeb/*.glb` generated by `deploy/scripts/optimize-web-glbs.sh` (`gltf-transform optimize --compress meshopt`, `EXT_meshopt_compression`, < 3.0 MiB). `ScadaWeb.csproj` links this tier to `wwwroot/models3d/`, and `DigitalTwin3D.cshtml` registers `MeshoptDecoder` (`three/addons/libs/meshopt_decoder.module.js`) via `gltfLoader.setMeshoptDecoder(MeshoptDecoder)`.
* **Operational Rule:** `Exported/` stays the single editable source of truth; `ExportedWeb/` is a regenerated build product. Gate 3 asserts `Exported/` has no compression extensions and `ExportedWeb/` has 24 valid `glTF` v2 files; Gate 5 asserts all 24 `/models3d/<name>.glb` endpoints return HTTP 200 and the headless WebGL page renders without the offline fallback.

### M-07 (2026-11-26): Zero-Draw-Call Instanced Sub-Equipment Kinematics
* **Decision:** Telemetry-driven motion for sub-equipment (`OM-101` overband magnet cross-belt, `CF-501` centrifuge dryer, `FP-501` filter press, `TLS-401` train loadout silo, `DC-101` dust collector) is emitted only through the **existing hardware-instanced buffers** (`_casterBoxes`, `_casterCylinders`, `_unlitBoxes`, `_coalLumps`) in `Plant3DOpenGlCanvas.cs`; no new draw calls, shaders or GL state changes.
* **Budget invariants:** Desktop OpenGL `LastFrameDrawCalls` stays at `80` (Gate 6 requires `< 100`) and `FlowAndPoseMicrosPerFrame < 500 us`; `ArchScada.Desktop --verify-deployment` reports `[3D TWIN]` GLB `24/24` parity with `Exported/*.glb`. Web3D mirrors the same motion in `DigitalTwin3D.cshtml`.

### M-08 (2026-11-26): Master OpenUSD Digital Twin Assembly (`ArchCoalPlant.usda`)
* **Artifact:** `ArchScada/Assets/3D/ArchCoalPlant.usda`, generated (never hand-edited) by `deploy/scripts/generate_openusd_plant.py`, run with the `openusd-mcp` interpreter: `/home/tim/.local/share/uv/tools/openusd-mcp/bin/python deploy/scripts/generate_openusd_plant.py`. Re-run it whenever `Plant3DSceneData.cs` changes.
* **Source of truth:** the generator parses `Plant3DSceneData.cs` (`MachineNodes`, `Conveyors`, `SubEquipmentNodes`, `PipeRoutes`) so the 111 `InteractiveNodes` (18 machines + 11 conveyors + 71 sub-equipment + 11 process pipes) keep their exact IDs, world transforms and SCADA links (`archscada:nodeId/displayName/category/scadaTag/stage/glbAsset/plcDeviceNum/...`). Prim names replace `-` with `_` (e.g. `/ArchCoalPlant/PrimaryEquipment/JC_101`). It aborts if the parsed counts differ from `18/11/71/11`.
* **Conventions:** Y-up, `metersPerUnit = 1`, defaultPrim `/ArchCoalPlant`; `rotateY(+yaw)` == `Matrix4x4.CreateRotationY(+yaw)`; conveyors use `translate(mid) + rotateY(atan2(-dz, dx))` with a tilted tail->head belt mesh; sub-equipment boxes sit on `Position.Y` and are `2 x HalfExtents` tall; pipes are octagonal tubes along the waypoints at `SamplePolyline(0.5)`; machines use box meshes derived from the `Exported/*.glb` accessor bounds (node transforms included).
* **Materials:** 10 `UsdPreviewSurface` PBR materials (`IndustrialSteel`, `SafetyYellow`, `CrusherBlue`, `ConveyorRubber`, `CoalAnthracite`, `ProcessWaterPiping`, `CopperBusbar`, `StatusRunGreen`, `StatusWarnAmber`, `StatusFaultRed`).
* **Variants:** every primary machine has `lod` (`high`/`medium`/`low`: body+plinth / body / bounding `Cube`) and `operatingState` (`running`/`warning`/`fault`/`standby`, switching the `StatusBeacon` material) variant sets.
* **GLB references:** USD core cannot compose `.glb`; each node carries `archscada:glbAsset` (string) and `archscada:glbAssetPath` (asset, `./Exported/<name>.glb`); the layer's `customLayerData.archscada.glbAssets` lists all 24 models (23 are used by nodes; `wheel_loader_bucket.glb` is the dynamic loader bucket articulated at runtime).
* **Tooling note (`openusd-mcp` actual API):** `inspect_scene(path)` (not `inspect_usd`), `get_prim`, and `export_mesh(path, prim_path, output)` which requires a `Mesh` prim (use `<node>/Body`, not the Xform or the root).
* **Verified (2026-11-26):** `scene_stats` = 284 prims / 129 meshes / 10 materials / 1058 faces, Y-up, 1.0 m/unit; `export_mesh` succeeds on the `Body` of all 111 nodes.

### M-09 (2026-11-26): ASP.NET Core Razor `ScriptTagHelper` vs. `<script type="importmap">` Root Cause & Fix
* **Symptom (discovered by Gate 5 Headless Chromium WebGL Assertion):** Headless browser threw `PAGEERROR: SyntaxError: Unexpected token ':'` followed by `CONSOLE [warning]: [DigitalTwin3D] Falling back to offline 2D GA canvas: Failed to resolve module specifier 'three'`, causing `#offline-banner` to display (`display: block`).
* **Root Cause:** `_ViewImports.cshtml` registers `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers`, which activates `Microsoft.AspNetCore.Mvc.TagHelpers.ScriptTagHelper` on every `<script>` element. Because `DigitalTwin3D.cshtml` sets `type="importmap"` without `src` or `asp-*` attributes, `ScriptTagHelper.Process()` stripped the `type="importmap"` attribute during Razor compilation, emitting a bare `<script>{ "imports": ... }</script>`. The browser parsed the JSON body as classic JavaScript (throwing `SyntaxError: Unexpected token ':'`), ignored the importmap, and then failed `await import("three")` with `TypeError: Failed to resolve module specifier 'three'`.
* **Fix:** Added `@removeTagHelper Microsoft.AspNetCore.Mvc.TagHelpers.ScriptTagHelper, Microsoft.AspNetCore.Mvc.TagHelpers` to the top of `ScadaWeb/ScadaWeb/ScadaWeb/Pages/DigitalTwin3D.cshtml` (line 3), preserving literal `<script type="importmap">` in the rendered HTML while maintaining compatibility with `Plant3DTests.DigitalTwin3D_Web3DAssets_Are100PercentOfflineVendored`.

### Action Ledger (`ACT-01` ... `ACT-06`, 2026-11-26)
| Action | Owner | Outcome / architecture decision |
| :--- | :--- | :--- |
| `ACT-01` | coordinator | Baseline `bf7eb86c2`: hardened 3D MCP toolchain, Web3D LOD/GLSL fixes (`M-03`/`M-04`). |
| `ACT-02` | `web-pipeline` | Gate 5 renders `/DigitalTwin3D` in headless Chromium (SwiftShader), verifies `24/24` GLBs loaded (`#kpi-glb === "24/24"`), checks the 24 `/models3d` endpoints, and caught the Razor `ScriptTagHelper` `importmap` stripping defect (`M-09`). |
| `ACT-03` | `kinematics-3d` | `--verify-deployment` GLB parity `16/16` -> `24/24` (`5.37 MiB`) + `SplashDeploymentChecksTests` (`M-07`). |
| `ACT-04` | `web-pipeline` | Dual-tier Meshopt Web3D GLBs (`1.15 MiB` total vs `5.37 MiB` uncompressed, `-78.6%`) via `deploy/scripts/optimize-web-glbs.sh` (`M-06`). |
| `ACT-05` | `kinematics-3d` | Instanced zero-draw-call sub-equipment kinematics (`LastFrameDrawCalls = 80 < 100`, `44.13 µs`) + `MeshoptDecoder` (`M-06`/`M-07`). |
| `ACT-06` | `openusd-memory` | Master OpenUSD assembly `ArchCoalPlant.usda` (`284` prims, `111` interactive nodes, `10` materials, `lod` & `operatingState` variants) (`M-08`) + this ledger. |
