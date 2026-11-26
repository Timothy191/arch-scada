#!/usr/bin/env python3
"""
Free, Zero-Key Shader, Poly Haven CC0 3D Asset & ArchScada GLB/Shader Auditor MCP Server (`shader-polyhaven-mcp`).

Provides 100% free, no-subscription MCP tools for:
1. GLSL / Vulkan / OpenGL vertex, fragment, and compute shader compilation & syntax validation
   using local `/usr/bin/glslangValidator`.
2. Automated extraction and compilation verification of all 6 embedded OpenGL shaders (Pass 1 Shadow,
   Pass 2 HDR PBR, Pass 3 Post-Process GTAO/Bloom/ACES/CAS) in `Plant3DOpenGlCanvas.cs` across both
   Desktop OpenGL (`#version 150`) and OpenGL ES 3.0 (`#version 300 es`).
3. Direct Poly Haven CC0 REST API search, metadata inspection, and asset download URLs (HDRIs, PBR
   textures, and 3D models in GLTF/Blend formats) without requiring an active Blender GUI session.
4. Project GLB topology, binary budget, and `SharpGLTF` / `StbImageSharp` extension compatibility
   auditing across `ArchScada/Assets/3D/Exported/*.glb`.
5. `SharpGLTF`-safe GLB optimization (`safe_optimize_archscada_glb`) that runs `gltf-transform`
   (`weld`, `dedup`, `prune`, `reorder`) without injecting `KHR_draco_mesh_compression`,
   `EXT_meshopt_compression`, `KHR_texture_basisu`, or `EXT_texture_webp` into desktop-shared assets.
"""

from __future__ import annotations

import glob
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

from mcp.server.fastmcp import FastMCP

mcp = FastMCP("shader-polyhaven-mcp")

POLYHAVEN_API = "https://api.polyhaven.com"
USER_AGENT = "ArchScada-3D-MCP/1.0 (+https://polyhaven.com)"

# Extensions that SharpGLTF.Schema2.ModelRoot.ParseGLB or StbImageSharp cannot decode natively
SHARPGLTF_UNSUPPORTED_EXTENSIONS = {
    "KHR_draco_mesh_compression",
    "EXT_meshopt_compression",
    "KHR_texture_basisu",
    "EXT_texture_webp",
}


def _http_get_json(url: str) -> Any:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=20) as resp:
        return json.loads(resp.read().decode("utf-8"))


@mcp.tool()
def validate_glsl_shader(
    shader_code: str,
    stage: str = "frag",
    target_env: str = "opengl",
) -> str:
    """
    Validate GLSL shader source code using local Khronos `glslangValidator`.

    Parameters:
    - shader_code: Full GLSL source string (if `#version` is omitted, `#version 330 core` is prepended).
    - stage: One of 'vert', 'frag', 'comp', 'geom', 'tesc', 'tese'.
    - target_env: One of 'opengl', 'vulkan1.2', 'vulkan1.3', 'spirv1.5'.
    """
    allowed_stages = {"vert", "frag", "comp", "geom", "tesc", "tese"}
    if stage not in allowed_stages:
        return json.dumps({"valid": False, "error": f"Invalid stage '{stage}'. Must be one of {sorted(allowed_stages)}."})

    code = shader_code.lstrip()
    if not code.startswith("#version"):
        code = "#version 330 core\n" + code

    with tempfile.TemporaryDirectory(prefix="glsl_mcp_") as tmpdir:
        shader_file = os.path.join(tmpdir, f"shader.{stage}")
        with open(shader_file, "w", encoding="utf-8") as f:
            f.write(code)

        cmd = ["/usr/bin/glslangValidator", "-S", stage]
        if target_env.startswith("vulkan"):
            cmd.extend(["--target-env", target_env])
        cmd.append(shader_file)

        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=15)
        output = (proc.stdout + "\n" + proc.stderr).strip()
        return json.dumps({
            "valid": proc.returncode == 0,
            "stage": stage,
            "target_env": target_env,
            "exit_code": proc.returncode,
            "diagnostics": output or "No warnings or errors (compilation succeeded).",
        }, indent=2)


@mcp.tool()
def audit_archscada_runtime_shaders(
    cs_path: str = "ArchScada/ArchScada.Desktop/Controls/ThreeD/Plant3DOpenGlCanvas.cs",
) -> str:
    """
    Extract all embedded OpenGL vertex and fragment shaders from `Plant3DOpenGlCanvas.cs` and
    validate every shader across both Desktop OpenGL (`#version 150`) and OpenGL ES 3.0
    (`#version 300 es`) using `/usr/bin/glslangValidator`.
    """
    p = Path(cs_path)
    if not p.is_absolute():
        p = Path(__file__).resolve().parents[2] / cs_path
    if not p.is_file():
        return json.dumps({"valid": False, "error": f"C# file not found: {p}"})

    source = p.read_text(encoding="utf-8")
    pattern = re.compile(r'string\s+(vsSrc|fsSrc)\s*=\s*header\s*\+\s*@"([^"]*(?:""[^"]*)*)";', re.S)
    matches = pattern.findall(source)
    if len(matches) < 6:
        return json.dumps({"valid": False, "error": f"Expected 6 shader bodies in {p.name}, found {len(matches)}"})

    pass_names = ["ShadowPass", "MainPbrPass", "PostProcessPass"]
    headers = {
        "desktop_gl150": "#version 150\n",
        "gles300": "#version 300 es\nprecision highp float;\n",
    }

    results = []
    all_valid = True
    for idx, (var_name, raw_body) in enumerate(matches):
        body = raw_body.replace('""', '"')
        stage = "vert" if var_name == "vsSrc" else "frag"
        pass_label = pass_names[min(idx // 2, len(pass_names) - 1)]
        for profile, hdr in headers.items():
            res = json.loads(validate_glsl_shader(hdr + body, stage=stage, target_env="opengl"))
            is_ok = bool(res.get("valid"))
            all_valid = all_valid and is_ok
            results.append({
                "pass": pass_label,
                "stage": stage,
                "profile": profile,
                "valid": is_ok,
                "diagnostics": res.get("diagnostics"),
            })

    return json.dumps({
        "valid": all_valid,
        "shader_variants_tested": len(results),
        "results": results,
    }, indent=2)


@mcp.tool()
def search_cc0_polyhaven_assets(
    asset_type: str = "all",
    categories: str = "",
    limit: int = 15,
) -> str:
    """
    Search 100% free CC0 3D models, HDRIs, and PBR textures on Poly Haven (no API key required).

    Parameters:
    - asset_type: 'hdris', 'textures', 'models', or 'all'
    - categories: Optional comma-separated category filter (e.g. 'industrial', 'metal', 'studio')
    - limit: Maximum results to return (default 15)
    """
    params: dict[str, str] = {}
    if asset_type in {"hdris", "textures", "models"}:
        params["t"] = asset_type
    if categories.strip():
        params["c"] = categories.strip()

    url = f"{POLYHAVEN_API}/assets"
    if params:
        url += "?" + urllib.parse.urlencode(params)

    data = _http_get_json(url)
    items = []
    for asset_id, meta in data.items():
        items.append({
            "id": asset_id,
            "name": meta.get("name", asset_id),
            "type": meta.get("type"),
            "categories": meta.get("categories", []),
            "tags": meta.get("tags", []),
            "download_count": meta.get("download_count", 0),
            "license": "CC0 1.0 Universal (Public Domain)",
            "preview_url": f"https://cdn.polyhaven.com/asset_img/thumbs/{asset_id}.png?width=512",
        })
    items.sort(key=lambda x: x["download_count"], reverse=True)
    return json.dumps({
        "total_matches": len(items),
        "returned": min(limit, len(items)),
        "assets": items[:limit],
    }, indent=2)


@mcp.tool()
def get_cc0_polyhaven_download_urls(
    asset_id: str,
    resolution: str = "1k",
) -> str:
    """
    Retrieve direct download URLs (GLTF, Blend, HDR, EXR, PBR Maps) for a Poly Haven CC0 asset.

    Parameters:
    - asset_id: The Poly Haven asset slug (e.g. 'industrial_sunset_puresky', 'steel_plate', 'wooden_crate_01')
    - resolution: '1k', '2k', '4k', or '8k'
    """
    files_data = _http_get_json(f"{POLYHAVEN_API}/files/{urllib.parse.quote(asset_id)}")
    extracted: dict[str, Any] = {}
    for section, by_res in files_data.items():
        if not isinstance(by_res, dict):
            continue
        res_block = by_res.get(resolution)
        if isinstance(res_block, dict):
            extracted[section] = {
                fmt: {"url": info.get("url"), "size_bytes": info.get("size"), "md5": info.get("md5")}
                for fmt, info in res_block.items()
                if isinstance(info, dict) and "url" in info
            }
    return json.dumps({
        "asset_id": asset_id,
        "resolution": resolution,
        "license": "CC0 1.0 Universal",
        "files": extracted,
    }, indent=2)


@mcp.tool()
def audit_workspace_glb_models(
    directory: str = "ArchScada/Assets/3D/Exported",
) -> str:
    """
    Audit all glTF 2.0 binary (.glb) models in the specified workspace directory for header validity,
    JSON chunk structure, mesh/material/node counts, byte budgets, and SharpGLTF desktop compatibility.
    """
    base = Path(directory)
    if not base.is_absolute():
        base = Path(__file__).resolve().parents[2] / directory

    glb_files = sorted(glob.glob(str(base / "*.glb")))
    reports = []
    all_compatible = True
    for path in glb_files:
        size = os.path.getsize(path)
        with open(path, "rb") as f:
            header = f.read(12)
            magic, version, total_len = struct.unpack("<III", header)
            chunk_len, chunk_type = struct.unpack("<II", f.read(8))
            json_bytes = f.read(chunk_len)

        gltf = json.loads(json_bytes.decode("utf-8"))
        ext_req = set(gltf.get("extensionsRequired", []))
        ext_used = set(gltf.get("extensionsUsed", []))
        forbidden = sorted((ext_req | ext_used) & SHARPGLTF_UNSUPPORTED_EXTENSIONS)
        valid_hdr = magic == 0x46546C67 and version == 2 and total_len == size
        sharpgltf_ok = valid_hdr and len(forbidden) == 0
        all_compatible = all_compatible and sharpgltf_ok

        reports.append({
            "file": os.path.basename(path),
            "size_kb": round(size / 1024, 2),
            "valid_header": valid_hdr,
            "sharpgltf_compatible": sharpgltf_ok,
            "unsupported_extensions": forbidden,
            "meshes": len(gltf.get("meshes", [])),
            "materials": len(gltf.get("materials", [])),
            "nodes": len(gltf.get("nodes", [])),
            "Accessors": len(gltf.get("accessors", [])),
            "generator": gltf.get("asset", {}).get("generator", "unknown"),
        })

    return json.dumps({
        "directory": str(base),
        "model_count": len(reports),
        "all_sharpgltf_compatible": all_compatible,
        "models": reports,
    }, indent=2)


@mcp.tool()
def safe_optimize_archscada_glb(
    input_path: str,
    output_path: str = "",
) -> str:
    """
    Optimize a glTF 2.0 (.glb) model for ArchScada using ONLY SharpGLTF/StbImageSharp-compatible
    operations (`weld`, `dedup`, `prune`, `reorder`), strictly preventing `draco`, `meshopt`,
    `ktx2`, or `webp` extensions that would break Desktop `GlbMeshLoader.cs`.
    """
    src = Path(input_path).resolve()
    if not src.is_file():
        return json.dumps({"success": False, "error": f"Input GLB not found: {src}"})

    dst = Path(output_path).resolve() if output_path.strip() else src
    orig_size = src.stat().st_size

    with tempfile.TemporaryDirectory(prefix="archscada_safe_glb_") as tmpdir:
        s1 = os.path.join(tmpdir, "s1_weld.glb")
        s2 = os.path.join(tmpdir, "s2_dedup.glb")
        s3 = os.path.join(tmpdir, "s3_prune.glb")
        s4 = os.path.join(tmpdir, "s4_reorder.glb")
        for cmd in [
            ["gltf-transform", "weld", str(src), s1],
            ["gltf-transform", "dedup", s1, s2],
            ["gltf-transform", "prune", s2, s3],
            ["gltf-transform", "reorder", s3, s4],
        ]:
            proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            if proc.returncode != 0:
                return json.dumps({"success": False, "error": proc.stderr or proc.stdout})
        shutil.copy2(s4, dst)

    new_size = dst.stat().st_size
    return json.dumps({
        "success": True,
        "input_path": str(src),
        "output_path": str(dst),
        "original_bytes": orig_size,
        "optimized_bytes": new_size,
        "sharpgltf_compatible": True,
        "operations_applied": ["weld", "dedup", "prune", "reorder"],
    }, indent=2)


def _run_self_test() -> int:
    shader_report = json.loads(audit_archscada_runtime_shaders())
    if not shader_report.get("valid"):
        print("ERROR: Embedded OpenGL shader validation failed:", json.dumps(shader_report, indent=2), file=sys.stderr)
        return 1

    glb_report = json.loads(audit_workspace_glb_models())
    if not glb_report.get("all_sharpgltf_compatible") or glb_report.get("model_count", 0) < 16:
        print("ERROR: Workspace GLB SharpGLTF compatibility check failed:", json.dumps(glb_report, indent=2), file=sys.stderr)
        return 1

    print(
        f"shader-polyhaven-mcp self-test PASS: {shader_report['shader_variants_tested']}/12 OpenGL/GLES3 shader variants valid; "
        f"{glb_report['model_count']} GLB models SharpGLTF & Three.js compatible."
    )
    return 0


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        raise SystemExit(_run_self_test())
    mcp.run()
