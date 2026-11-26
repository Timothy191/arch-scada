# Implementation Plan: 3d_mcp_hardening_20261126

## Phase 1: Web3D & Asset Processing MCP Fixes (P0)
- [x] Task: TODO-01 — Fix JavaScript syntax error, `THREE.LOD` double-offset transform bug, and runtime Three.js API defects in `ScadaWeb/ScadaWeb/ScadaWeb/Pages/DigitalTwin3D.cshtml`
  - [x] Remove duplicate closing brace and orphaned `bScale` assignment at lines 2844–2852
  - [x] Fix `THREE.LOD` local coordinates (`position.set(0, m.half[1], 0)`) and single parenting at lines 2540–2582
  - [x] Remove non-existent `new THREE.LODManager()` and `new THREE.WebGLShadowMap()` calls that triggered `catch (err) { startOfflineFallback(...) }` at runtime
  - [x] Hoist `let bloomPass = null;` to outer scope so `[data-toggles="bloom"]` handler never throws `ReferenceError`
  - [x] Remove conflicting duplicate `#camera-bar` event listener and upgrade `clearcoat`-bearing materials from `THREE.MeshStandardMaterial` to `THREE.MeshPhysicalMaterial`
  - [x] Verify all `<script>` blocks in `DigitalTwin3D.cshtml` pass JSON and `node --check` validation
- [x] Task: TODO-02 — Patch `3d-asset-processing-mcp` validator bug, expand multi-step planner to all 35 `gltf-transform` operations, and quarantine `*_stepN_*.glb` intermediate files to `os.tmpdir()`
  - [x] Patch `validate_model` in `/home/tim/.local/lib/node_modules/3d-asset-processing-mcp/dist/core/validator-simple.js` to handle `validatorResult.issues.messages`
  - [x] Expand `planExecutionSteps` and `executeMultiStep` in `gltf-transform-executor.js` to support all 35 `gltf-transform` operations (`center`, `flatten`, `instance`, `join`, `metalrough`, `palette`, `unlit`, `dequantize`, `unweld`, `weld`, `tangents`, `unwrap`, `simplify`, `reorder`, `quantize`, `meshopt`, `draco`, `ktxfix`, `ktxdecompress`, `resize`, `etc1s`, `uastc`, `webp`, `avif`, `png`, `jpeg`, `resample`, `sequence`, `sparse`, `optimize`, `dedup`, `prune`, `partition`, `gzip`)
  - [x] Store intermediate step files in `os.tmpdir()` with `try/finally` cleanup
  - [x] Verify `validate_model` and `gltf-transform-executor` leave zero `*_step*.glb` files in `ArchScada/Assets/3D/Exported/`

## Phase 2: OpenUSD, Blender & Shader MCP Hardening (P1)
- [x] Task: TODO-03 — Patch `openusd-mcp` for persistent `usd_set_variant` disk saves, world-space `usd_export_mesh`, and automatic parent directory creation
  - [x] Update `set_variant()` in `openusd_mcp/tools.py` to call `stage.GetRootLayer().Save()`
  - [x] Update `export_mesh()` in `openusd_mcp/tools.py` to apply `ComputeLocalToWorldTransform()`, auto-create output parent directories, and format deterministic 6-decimal OBJ coordinates
  - [x] Verify persistent variant selection and world-space mesh export via automated test
- [x] Task: TODO-04 — Harden `blender-mcp` telemetry privacy, headless `get_viewport_screenshot` fallback, and version-controlled daemon script
  - [x] Disable outbound telemetry in `blender_mcp/config.py` (`enabled: bool = False`)
  - [x] Add headless `--background` render fallback (`bpy.ops.render.render(write_still=True)`) to `get_viewport_screenshot` in `~/.config/blender/5.2/scripts/addons/blender_mcp.py`
  - [x] Install version-controlled `deploy/scripts/start-blender-mcp-headless.sh` and verify headless screenshot capture over MCP
- [x] Task: TODO-05 — Promote `shader_polyhaven_mcp.py` to `deploy/scripts/` and add C# OpenGL (`#version 150` & `#version 300 es`) shader extraction, `SharpGLTF` extension guard, and safe GLB optimizer
  - [x] Create `deploy/scripts/shader_polyhaven_mcp.py` with `audit_archscada_runtime_shaders`, `safe_optimize_archscada_glb`, SharpGLTF extension check in `audit_workspace_glb_models`, and `--self-test` CLI flag
  - [x] Fix Desktop OpenGL `#version 150` built-in `noise3` function overload conflict in `Plant3DOpenGlCanvas.cs` (`vnoise3`)
  - [x] Update `.mcp.json` and `.agents/mcp/shader_polyhaven_mcp.py`
  - [x] Verify `deploy/scripts/shader_polyhaven_mcp.py --self-test` passes 100% (12/12 shader variants, 24/24 GLB models)

## Phase 3: Quality Gate Integration, Unit Test Completion & Full 6-Gate Verification (P2)
- [x] Task: TODO-06 — Complete `ThreeDSyncTests.cs` assertions, make `verify-quality-gates.sh` interpreter paths portable, and execute all 6 quality gates
  - [x] Complete `BaseXmlDevices_ShouldHaveCorresponding3DRepresentation` and add `ExportedGlbModels_MustAllLoadViaSharpGltfWithValidGeometry` in `tests/ArchScada.Desktop.Tests/ThreeDSyncTests.cs` (3/3 xUnit tests passing)
  - [x] Replace hardcoded `/home/tim/...` Python path in `verify-quality-gates.sh` with portable `${BLENDER_MCP_PYTHON:-$HOME/.local/share/uv/tools/blender-mcp/bin/python}` fallback to `python3`
  - [x] Run `./verify-quality-gates.sh` and verify all 6 gates pass with 0 errors and 0 warnings
  - [x] Update Conductor track plan and registry to `[x]` complete
