#!/usr/bin/env python3
"""Generate the master OpenUSD plant assembly ``ArchScada/Assets/3D/ArchCoalPlant.usda``.

Run with the Pixar ``usd-core`` interpreter of the ``openusd-mcp`` uv tool:

    /home/tim/.local/share/uv/tools/openusd-mcp/bin/python deploy/scripts/generate_openusd_plant.py

Single source of truth: ``ArchScada.Desktop/Controls/ThreeD/Plant3DSceneData.cs`` (read-only).
The generator parses the C# node tables (``MachineNodes``, ``Conveyors``, ``SubEquipmentNodes``,
``PipeRoutes``) so every one of the 111 ``InteractiveNodes`` (18 machines + 11 conveyors +
71 sub-equipment + 11 process pipes) is authored with the exact world transform used by the
Desktop OpenGL canvas and the ScadaWeb Three.js twin:

* Y-up, 1 unit = 1 m, right-handed, ``rotateY(+yaw)`` == ``Matrix4x4.CreateRotationY(+yaw)``.
* Machines : GLB-bounds ``Mesh`` box (``Exported/*.glb`` accessor min/max incl. node transforms),
             ``translate / rotateY / scale`` ops, ``lod`` + ``operatingState`` variant sets.
* Conveyors: ``translate(mid) / rotateY(atan2(-dz, dx))`` + tilted belt ``Mesh`` tail -> head.
* Sub-eq.  : ``translate(Position)`` + ``Mesh`` box (base at Position.Y, 2 x HalfExtents tall).
* Pipes    : ``translate(SamplePolyline(0.5))`` + octagonal tube ``Mesh`` along the waypoints.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import struct
import sys
from pathlib import Path

from pxr import Gf, Sdf, Usd, UsdGeom, UsdShade, Vt

ROOT = Path(__file__).resolve().parents[2]
SCENE_CS = ROOT / "ArchScada/ArchScada.Desktop/Controls/ThreeD/Plant3DSceneData.cs"
GLB_DIR = ROOT / "ArchScada/Assets/3D/Exported"
DEFAULT_OUT = ROOT / "ArchScada/Assets/3D/ArchCoalPlant.usda"

EXPECTED = {"machines": 18, "conveyors": 11, "sub": 71, "pipes": 11}


# --------------------------------------------------------------------------- C# parsing helpers
def strip_comments(src: str) -> str:
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            j = i + 1
            while j < n and src[j] != '"':
                j += 2 if src[j] == "\\" else 1
            out.append(src[i : j + 1])
            i = j + 1
        elif src.startswith("//", i):
            while i < n and src[i] != "\n":
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def split_args(src: str, open_idx: int) -> list[str]:
    """Split the top-level args of the call whose '(' is at ``open_idx``."""
    depth, i, n, cur, args = 0, open_idx, len(src), [], []
    while i < n:
        c = src[i]
        if c == '"':
            j = i + 1
            while src[j] != '"':
                j += 2 if src[j] == "\\" else 1
            cur.append(src[i : j + 1])
            i = j + 1
            continue
        if c in "({[":
            depth += 1
            if depth > 1:
                cur.append(c)
        elif c in ")}]":
            depth -= 1
            if depth == 0:
                args.append("".join(cur).strip())
                return [a for a in args if a != ""]
            cur.append(c)
        elif c == "," and depth == 1:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    raise ValueError("unbalanced parentheses")


def cs_str(v: str) -> str:
    v = v.strip()
    assert v.startswith('"') and v.endswith('"'), v
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), v[1:-1])


def cs_float(v: str) -> float:
    return float(v.strip().rstrip("fF"))


def cs_vec(v: str) -> tuple[float, float, float]:
    v = v.strip()
    if v in ("Vector3.One",):
        return (1.0, 1.0, 1.0)
    if v in ("Vector3.Zero",):
        return (0.0, 0.0, 0.0)
    m = re.fullmatch(r"new(?:\s*Vector3)?\s*\(([^)]*)\)", v)
    assert m, v
    p = [cs_float(x) for x in m.group(1).split(",")]
    return (p[0], p[1], p[2])


def cs_strlist(v: str) -> list[str]:
    return re.findall(r'"((?:[^"\\]|\\.)*)"', v)


def cs_color(v: str) -> str:
    m = re.search(r'"(#[0-9A-Fa-f]{6,8})"', v)
    assert m, v
    return m.group(1)


def named(args: list[str]) -> tuple[list[str], dict[str, str]]:
    pos, kw = [], {}
    for a in args:
        m = re.match(r"^([A-Za-z_]\w*)\s*:\s*(.*)$", a, re.S)
        if m:
            kw[m.group(1)] = m.group(2).strip()
        else:
            pos.append(a)
    return pos, kw


def hex_rgb(h: str) -> tuple[float, float, float]:
    h = h.lstrip("#")[-6:]
    return tuple(int(h[i : i + 2], 16) / 255.0 for i in (0, 2, 4))  # type: ignore[return-value]


def parse_scene(src: str) -> dict:
    src = strip_comments(src)

    def region(start: str, end: str) -> str:
        a = src.index(start)
        return src[a : src.index(end, a)]

    mat_colors = dict(re.findall(r'CoalMaterialClass\.(\w+)\s*=>\s*Color\.Parse\("(#\w+)"\)', src))
    med_colors = dict(re.findall(r'FlowMedium\.(\w+)\s*=>\s*Color\.Parse\("(#\w+)"\)', src))
    default_med = re.search(r'_\s*=>\s*Color\.Parse\("(#\w+)"\)\s*;\s*public static Color GetMaterialColor', src, re.S)
    med_colors.setdefault("Slurry", default_med.group(1) if default_med else "#38BDF8")

    # PLC channel map ("Direct PLC: ROM101 (Modbus ...)")
    plc = {}
    body = region("public static string GetPlcChannelMapping", "FormatProvisionalConfigNote")
    for ids, mapping in re.findall(r'((?:"[\w-]+"\s*(?:or\s*)?)+)=>\s*"([^"]*)"', body):
        for i in re.findall(r'"([\w-]+)"', ids):
            plc[i.upper()] = mapping

    machines = []
    mreg = region("MachineNodes = new Plant3DNode[]", "IReadOnlyList<ConveyorSpec> Conveyors")
    for m in re.finditer(r"new\(\s*Id:", mreg):
        _, kw = named(split_args(mreg, mreg.index("(", m.start())))
        machines.append(
            dict(
                id=cs_str(kw["Id"]), ga=cs_str(kw["GaLabel"]), stage=cs_str(kw["StageLabel"]),
                type=cs_str(kw["EquipmentType"]), glb=cs_str(kw["GlbFileName"]),
                pos=cs_vec(kw["Position"]), yaw=cs_float(kw["YawDegrees"]), scale=cs_vec(kw["Scale"]),
                half=cs_vec(kw["HalfExtents"]), color=cs_color(kw["DefaultColor"]),
                spec=cs_str(kw["DimensionsSpec"]), principle=cs_str(kw["MechanicalPrinciple"]),
                desc=cs_str(kw["ComponentDescription"]), up=cs_strlist(kw["UpstreamIds"]),
                down=cs_strlist(kw["DownstreamIds"]), scada=cs_str(kw["LinkedScadaId"]),
                kind="machine",
            )
        )

    conveyors = []
    creg = region("IReadOnlyList<ConveyorSpec> Conveyors", "ConveyorNodes = BuildConveyorNodes")
    for m in re.finditer(r"new\(\s*Id:", creg):
        _, kw = named(split_args(creg, creg.index("(", m.start())))
        mc = kw["MaterialClass"].split(".")[-1]
        conveyors.append(
            dict(
                id=cs_str(kw["Id"]), ga=cs_str(kw["GaLabel"]), stage=cs_str(kw["StageLabel"]),
                tail=cs_vec(kw["TailPos"]), head=cs_vec(kw["HeadPos"]), width=cs_float(kw["Width"]),
                drop_y=cs_float(kw["HeadDropTargetY"]), material_class=mc,
                color=mat_colors.get(mc, "#14171C"), scada=cs_str(kw["LinkedScadaId"]),
                up=cs_strlist(kw["UpstreamIds"]), down=cs_strlist(kw["DownstreamIds"]),
                spec=cs_str(kw["DimensionsSpec"]), principle=cs_str(kw["MechanicalPrinciple"]),
                desc=cs_str(kw["ComponentDescription"]), kind="conveyor",
                type=f"Belt Conveyor ({mc})", glb="conveyor_segment.glb",
            )
        )

    sub = []
    sreg = region("SubEquipmentNodes = new Plant3DNode[]", "PipeNodes = BuildPipeNodes")
    default_stage = "Wash Plant & Medium Recovery (WP-301)"
    for m in re.finditer(r'SubNode\(\s*"', sreg):
        pos, kw = named(split_args(sreg, sreg.index("(", m.start())))
        names = ["id", "ga", "type", "pos", "half", "label_h", "color", "spec", "principle", "desc",
                 "up", "down", "scada", "stage", "glb", "yaw"]
        a = dict(zip(names, pos))
        a.update({{"Id": "id"}.get(k, k): v for k, v in kw.items()})
        sub.append(
            dict(
                id=cs_str(a["id"]), ga=cs_str(a["ga"]), type=cs_str(a["type"]), pos=cs_vec(a["pos"]),
                half=cs_vec(a["half"]), color=cs_color(a["color"]), spec=cs_str(a["spec"]),
                principle=cs_str(a["principle"]), desc=cs_str(a["desc"]), up=cs_strlist(a["up"]),
                down=cs_strlist(a["down"]), scada=cs_str(a["scada"]),
                stage=cs_str(a["stage"]) if "stage" in a else default_stage,
                glb=cs_str(a["glb"]) if "glb" in a else "", yaw=cs_float(a["yaw"]) if "yaw" in a else 0.0,
                kind="sub",
            )
        )

    pipes = []
    preg = region("PipeRoutes = new PipeRoute[]", "SubEquipmentNodes = new Plant3DNode[]")
    for m in re.finditer(r'new\("', preg):
        pos, kw = named(split_args(preg, preg.index("(", m.start())))
        if "Id" not in kw or not cs_str(kw["Id"]).strip():
            continue
        medium = pos[1].split(".")[-1]
        pts = [tuple(cs_float(x) for x in p.split(",")) for p in re.findall(r"new\(([^)]*)\)", pos[3])]
        pid = cs_str(kw["Id"])
        scada = {"PIPE-303": "TK-301", "PIPE-305": "TK-201", "PIPE-308": "PP-203", "PIPE-309": "PP-203",
                 "PIPE-310": "SC-102", "PIPE-311": "PP-202"}.get(pid, cs_str(pos[0]))
        pipes.append(
            dict(
                id=pid, ga=cs_str(kw.get("GaLabel", '""')), type=f"Process Pipe ({medium})",
                stage=cs_str(kw["StageLabel"]) if "StageLabel" in kw else "Wash Plant & Medium Recovery",
                medium=medium, radius=cs_float(pos[2]), pts=pts, color=med_colors.get(medium, "#38BDF8"),
                spec=cs_str(kw.get("DimensionsSpec", '""')), principle=cs_str(kw.get("MechanicalPrinciple", '""')),
                desc=cs_str(kw.get("ComponentDescription", '""')),
                up=cs_strlist(kw.get("UpstreamIds", "")), down=cs_strlist(kw.get("DownstreamIds", "")),
                scada=scada, kind="pipe",
            )
        )
    return dict(machines=machines, conveyors=conveyors, sub=sub, pipes=pipes, plc=plc)


# --------------------------------------------------------------------------- GLB bounds
def glb_bounds(path: Path):
    """World-space (scene root) POSITION bounds of a .glb, honoring node transforms."""
    data = path.read_bytes()
    magic, _ver, _len = struct.unpack_from("<4sII", data, 0)
    assert magic == b"glTF", path
    clen, ctype = struct.unpack_from("<II", data, 12)
    assert ctype == 0x4E4F534A
    g = json.loads(data[20 : 20 + clen])
    nodes = g.get("nodes", [])

    def local(n):
        if "matrix" in n:
            m = n["matrix"]  # column-major -> Gf row-vector matrix is its transpose-compatible layout
            return Gf.Matrix4d(*m)
        t = n.get("translation", [0, 0, 0])
        r = n.get("rotation", [0, 0, 0, 1])
        s = n.get("scale", [1, 1, 1])
        mt = Gf.Matrix4d(1).SetTranslate(Gf.Vec3d(*t))
        mr = Gf.Matrix4d(1).SetRotate(Gf.Rotation(Gf.Quatd(r[3], Gf.Vec3d(r[0], r[1], r[2]))))
        ms = Gf.Matrix4d(1).SetScale(Gf.Vec3d(*s))
        return ms * mr * mt  # USD row-vector convention: scale, rotate, translate

    lo, hi = [math.inf] * 3, [-math.inf] * 3

    def walk(i, parent):
        n = nodes[i]
        w = local(n) * parent
        if "mesh" in n:
            for prim in g["meshes"][n["mesh"]]["primitives"]:
                acc = g["accessors"][prim["attributes"]["POSITION"]]
                if "min" not in acc or "max" not in acc:
                    continue
                for cx in (acc["min"][0], acc["max"][0]):
                    for cy in (acc["min"][1], acc["max"][1]):
                        for cz in (acc["min"][2], acc["max"][2]):
                            p = w.Transform(Gf.Vec3d(cx, cy, cz))
                            for k in range(3):
                                lo[k], hi[k] = min(lo[k], p[k]), max(hi[k], p[k])
        for c in n.get("children", []):
            walk(c, w)

    scene = g["scenes"][g.get("scene", 0)]
    for r in scene["nodes"]:
        walk(r, Gf.Matrix4d(1))
    if lo[0] is math.inf:
        return None
    return tuple(lo), tuple(hi)


# --------------------------------------------------------------------------- USD helpers
def sanitize(node_id: str) -> str:
    return re.sub(r"[^A-Za-z0-9_]", "_", node_id)


def box_points(lo, hi):
    (x0, y0, z0), (x1, y1, z1) = lo, hi
    return [Gf.Vec3f(x0, y0, z0), Gf.Vec3f(x1, y0, z0), Gf.Vec3f(x1, y1, z0), Gf.Vec3f(x0, y1, z0),
            Gf.Vec3f(x0, y0, z1), Gf.Vec3f(x1, y0, z1), Gf.Vec3f(x1, y1, z1), Gf.Vec3f(x0, y1, z1)]


BOX_FACES = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 4, 7, 3), (1, 2, 6, 5), (0, 1, 5, 4), (3, 7, 6, 2)]


def define_mesh(stage, path, points, faces, color=None):
    mesh = UsdGeom.Mesh.Define(stage, path)
    mesh.CreatePointsAttr(Vt.Vec3fArray(points))
    mesh.CreateFaceVertexCountsAttr(Vt.IntArray([len(f) for f in faces]))
    mesh.CreateFaceVertexIndicesAttr(Vt.IntArray([i for f in faces for i in f]))
    mesh.CreateSubdivisionSchemeAttr(UsdGeom.Tokens.none)
    mesh.CreateDoubleSidedAttr(True)
    mesh.CreateExtentAttr(Vt.Vec3fArray(_extent(points)))
    if color:
        mesh.CreateDisplayColorAttr(Vt.Vec3fArray([Gf.Vec3f(*hex_rgb(color))]))
    return mesh


def _extent(points):
    xs, ys, zs = zip(*[(p[0], p[1], p[2]) for p in points])
    return [Gf.Vec3f(min(xs), min(ys), min(zs)), Gf.Vec3f(max(xs), max(ys), max(zs))]


def define_box(stage, path, lo, hi, color=None):
    return define_mesh(stage, path, box_points(lo, hi), BOX_FACES, color)


def tube_mesh(pts, radius, sides=8):
    """Octagonal tube along an orthogonal/arbitrary polyline; ends of interior joints overlap by r."""
    verts, faces = [], []
    last = len(pts) - 2
    for s in range(len(pts) - 1):
        a, b = Gf.Vec3d(*pts[s]), Gf.Vec3d(*pts[s + 1])
        d = b - a
        ln = d.GetLength()
        if ln < 1e-6:
            continue
        u = d / ln
        if s > 0:
            a = a - u * radius
        if s < last:
            b = b + u * radius
        ref = Gf.Vec3d(0, 1, 0) if abs(u[1]) < 0.9 else Gf.Vec3d(1, 0, 0)
        v = Gf.Cross(u, ref).GetNormalized()
        w = Gf.Cross(u, v).GetNormalized()
        base = len(verts)
        for end in (a, b):
            for k in range(sides):
                t = 2 * math.pi * k / sides
                p = end + (v * math.cos(t) + w * math.sin(t)) * radius
                verts.append(p)
        for k in range(sides):
            k2 = (k + 1) % sides
            faces.append((base + k, base + k2, base + sides + k2, base + sides + k))
        faces.append(tuple(base + k for k in reversed(range(sides))))
        faces.append(tuple(base + sides + k for k in range(sides)))
    return verts, faces


# --------------------------------------------------------------------------- materials
MATERIALS = {
    # name: (diffuse, metallic, roughness, clearcoat, emissive)
    "IndustrialSteel": ((0.55, 0.58, 0.62), 0.85, 0.45, 0.0, None),
    "SafetyYellow": ((0.92, 0.70, 0.03), 0.10, 0.50, 0.0, None),
    "CrusherBlue": ((0.01, 0.52, 0.78), 0.60, 0.40, 0.1, None),
    "ConveyorRubber": ((0.06, 0.06, 0.07), 0.00, 0.90, 0.0, None),
    "CoalAnthracite": ((0.05, 0.055, 0.065), 0.05, 0.75, 0.2, None),
    "ProcessWaterPiping": ((0.22, 0.74, 0.97), 0.30, 0.35, 0.3, None),
    "CopperBusbar": ((0.72, 0.45, 0.20), 1.00, 0.30, 0.0, None),
    "StatusRunGreen": ((0.13, 0.77, 0.37), 0.00, 0.40, 0.0, (0.05, 0.60, 0.20)),
    "StatusWarnAmber": ((0.96, 0.62, 0.04), 0.00, 0.40, 0.0, (0.70, 0.40, 0.00)),
    "StatusFaultRed": ((0.94, 0.27, 0.27), 0.00, 0.40, 0.0, (0.80, 0.10, 0.10)),
}


def author_materials(stage, root):
    UsdGeom.Scope.Define(stage, f"{root}/Materials")
    out = {}
    for name, (rgb, metal, rough, coat, emis) in MATERIALS.items():
        mat = UsdShade.Material.Define(stage, f"{root}/Materials/{name}")
        sh = UsdShade.Shader.Define(stage, f"{root}/Materials/{name}/PreviewSurface")
        sh.CreateIdAttr("UsdPreviewSurface")
        sh.CreateInput("diffuseColor", Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*rgb))
        sh.CreateInput("metallic", Sdf.ValueTypeNames.Float).Set(metal)
        sh.CreateInput("roughness", Sdf.ValueTypeNames.Float).Set(rough)
        sh.CreateInput("clearcoat", Sdf.ValueTypeNames.Float).Set(coat)
        sh.CreateInput("opacity", Sdf.ValueTypeNames.Float).Set(1.0)
        if emis:
            sh.CreateInput("emissiveColor", Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*emis))
        mat.CreateSurfaceOutput().ConnectToSource(sh.ConnectableAPI(), "surface")
        out[name] = mat
    return out


def pick_material(n) -> str:
    if n["kind"] == "conveyor":
        return "ConveyorRubber"
    if n["kind"] == "pipe":
        return "ProcessWaterPiping"
    t = f'{n["type"]} {n["ga"]} {n["id"]}'.lower()
    if any(k in t for k in ("stockpile", "coal heap", "product pile")):
        return "CoalAnthracite"
    if any(k in t for k in ("crusher", "roll", "jaw")):
        return "CrusherBlue"
    if any(k in t for k in ("mcc", "substation", "transformer", "e-house", "busbar", "vfd", "electrical", "switchgear")):
        return "CopperBusbar"
    if any(k in t for k in ("loader", "walkway", "ramp", "guard", "hopper", "bin ", "feeder bin")):
        return "SafetyYellow"
    if any(k in t for k in ("pump", "pipe", "valve", "spray", "sump", "drip", "manifold", "water", "launder", "slurry")):
        return "ProcessWaterPiping"
    return "IndustrialSteel"


def bind(stage, prim_path, mat):
    prim = stage.GetPrimAtPath(prim_path) or stage.OverridePrim(prim_path)
    UsdShade.MaterialBindingAPI.Apply(prim).Bind(mat)


def set_scada_attrs(prim, n, plc_map, glb_exists):
    S, T = Sdf.ValueTypeNames, None
    prim.CreateAttribute("archscada:nodeId", S.String).Set(n["id"])
    prim.CreateAttribute("archscada:displayName", S.String).Set(n["ga"])
    prim.CreateAttribute("archscada:category", S.String).Set(n["type"])
    prim.CreateAttribute("archscada:scadaTag", S.String).Set(n["scada"])
    prim.CreateAttribute("archscada:stage", S.String).Set(n["stage"])
    prim.CreateAttribute("archscada:nodeKind", S.String).Set(n["kind"])
    prim.CreateAttribute("archscada:glbAsset", S.String).Set(n.get("glb", "") or "")
    if n.get("glb") and glb_exists:
        prim.CreateAttribute("archscada:glbAssetPath", S.Asset).Set(Sdf.AssetPath(f"./Exported/{n['glb']}"))
    mapping = plc_map.get(n["id"].upper(), "")
    m = re.match(r"Direct PLC:\s*(\S+)", mapping)
    prim.CreateAttribute("archscada:plcDeviceNum", S.String).Set(m.group(1) if m else "DERIVED")
    prim.CreateAttribute("archscada:plcChannelMapping", S.String).Set(
        mapping or f"Derived Process Model (coupled to {n['scada']})")
    prim.CreateAttribute("archscada:dimensionsSpec", S.String).Set(n["spec"])
    prim.CreateAttribute("archscada:mechanicalPrinciple", S.String).Set(n["principle"])
    prim.CreateAttribute("archscada:description", S.String).Set(n["desc"])
    prim.CreateAttribute("archscada:upstreamIds", S.StringArray).Set(Vt.StringArray(n["up"]))
    prim.CreateAttribute("archscada:downstreamIds", S.StringArray).Set(Vt.StringArray(n["down"]))


def xform_trs(x: UsdGeom.Xformable, pos, yaw=None, scale=None):
    x.AddTranslateOp().Set(Gf.Vec3d(*pos))
    if yaw is not None:
        x.AddRotateYOp().Set(float(yaw))
    if scale is not None:
        x.AddScaleOp().Set(Gf.Vec3f(*scale))


# --------------------------------------------------------------------------- authoring
def author_machine(stage, root, n, mats, plc, bounds_cache):
    path = f"{root}/PrimaryEquipment/{sanitize(n['id'])}"
    xf = UsdGeom.Xform.Define(stage, path)
    sx, sy, sz = n["scale"]
    xform_trs(xf, n["pos"], n["yaw"], n["scale"])
    glb_path = GLB_DIR / n["glb"] if n["glb"] else None
    glb_exists = bool(glb_path and glb_path.exists())
    b = bounds_cache.get(n["glb"]) if glb_exists else None
    if glb_exists and n["glb"] not in bounds_cache:
        b = bounds_cache[n["glb"]] = glb_bounds(glb_path)
    if b:
        lo, hi = b
        n["bounds_source"] = "glb"
    else:  # fallback: HalfExtents box on the Position base, compensating for the xform scale
        hx, hy, hz = n["half"]
        lo, hi = (-hx / sx, 0.0, -hz / sz), (hx / sx, 2 * hy / sy, hz / sz)
        n["bounds_source"] = "halfExtents"
    set_scada_attrs(xf.GetPrim(), n, plc, glb_exists)
    prim = xf.GetPrim()
    prim.CreateAttribute("archscada:boundsSource", Sdf.ValueTypeNames.String).Set(n["bounds_source"])
    prim.CreateAttribute("archscada:glbBoundsMin", Sdf.ValueTypeNames.Float3).Set(Gf.Vec3f(*lo))
    prim.CreateAttribute("archscada:glbBoundsMax", Sdf.ValueTypeNames.Float3).Set(Gf.Vec3f(*hi))
    prim.CreateAttribute("archscada:defaultColor", Sdf.ValueTypeNames.Color3f).Set(Gf.Vec3f(*hex_rgb(n["color"])))
    mat = mats[pick_material(n)]

    # Status beacon (outside variants; its binding is switched by operatingState)
    bc = UsdGeom.Sphere.Define(stage, f"{path}/StatusBeacon")
    bc.CreateRadiusAttr(0.18)
    bxf = UsdGeom.Xformable(bc)
    bxf.AddTranslateOp().Set(Gf.Vec3d((lo[0] + hi[0]) / 2, hi[1] + 0.35 / sy, (lo[2] + hi[2]) / 2))
    bxf.AddScaleOp().Set(Gf.Vec3f(1 / sx, 1 / sy, 1 / sz))
    bc.CreateExtentAttr(Vt.Vec3fArray([Gf.Vec3f(-0.18, -0.18, -0.18), Gf.Vec3f(0.18, 0.18, 0.18)]))

    # lod variant set
    lod = prim.GetVariantSets().AddVariantSet("lod")
    for v in ("high", "medium", "low"):
        lod.AddVariant(v)
        lod.SetVariantSelection(v)
        with lod.GetVariantEditContext():
            if v == "low":
                cube = UsdGeom.Cube.Define(stage, f"{path}/BoundingBox")
                cube.CreateSizeAttr(1.0)
                cx = UsdGeom.Xformable(cube)
                cx.AddTranslateOp().Set(Gf.Vec3d(*[(lo[k] + hi[k]) / 2 for k in range(3)]))
                cx.AddScaleOp().Set(Gf.Vec3f(*[(hi[k] - lo[k]) for k in range(3)]))
                cube.CreateDisplayColorAttr(Vt.Vec3fArray([Gf.Vec3f(*hex_rgb(n["color"]))]))
                UsdShade.MaterialBindingAPI.Apply(cube.GetPrim()).Bind(mat)
            else:
                body = define_box(stage, f"{path}/Body", lo, hi, n["color"])
                UsdShade.MaterialBindingAPI.Apply(body.GetPrim()).Bind(mat)
                if v == "high":  # extra detail: base plinth (skid / foundation frame)
                    pl = define_box(stage, f"{path}/Plinth", (lo[0], lo[1], lo[2]),
                                    (hi[0], lo[1] + 0.12 / sy, hi[2]), "#3A4048")
                    UsdShade.MaterialBindingAPI.Apply(pl.GetPrim()).Bind(mats["IndustrialSteel"])
    lod.SetVariantSelection("high")

    # operatingState variant set
    st = prim.GetVariantSets().AddVariantSet("operatingState")
    state_mat = {"running": "StatusRunGreen", "warning": "StatusWarnAmber",
                 "fault": "StatusFaultRed", "standby": "IndustrialSteel"}
    for v, mname in state_mat.items():
        st.AddVariant(v)
        st.SetVariantSelection(v)
        with st.GetVariantEditContext():
            prim.CreateAttribute("archscada:operatingState", Sdf.ValueTypeNames.Token).Set(v)
            UsdShade.MaterialBindingAPI.Apply(stage.OverridePrim(f"{path}/StatusBeacon")).Bind(mats[mname])
    st.SetVariantSelection("running")


def author_conveyor(stage, root, n, mats, plc):
    path = f"{root}/Conveyors/{sanitize(n['id'])}"
    tail, head = Gf.Vec3d(*n["tail"]), Gf.Vec3d(*n["head"])
    mid, diff = (tail + head) * 0.5, head - tail
    yaw = math.degrees(math.atan2(-diff[2], diff[0]))
    xf = UsdGeom.Xform.Define(stage, path)
    xform_trs(xf, tuple(mid), yaw)
    n["pos"], n["yaw"] = tuple(mid), yaw
    set_scada_attrs(xf.GetPrim(), n, plc, (GLB_DIR / n["glb"]).exists())
    p = xf.GetPrim()
    S = Sdf.ValueTypeNames
    p.CreateAttribute("archscada:tailPos", S.Float3).Set(Gf.Vec3f(*n["tail"]))
    p.CreateAttribute("archscada:headPos", S.Float3).Set(Gf.Vec3f(*n["head"]))
    p.CreateAttribute("archscada:centerPos", S.Float3).Set(Gf.Vec3f(*mid))
    p.CreateAttribute("archscada:beltWidth", S.Float).Set(n["width"])
    p.CreateAttribute("archscada:headDropTargetY", S.Float).Set(n["drop_y"])
    p.CreateAttribute("archscada:materialClass", S.String).Set(n["material_class"])
    h = math.hypot(diff[0], diff[2])
    hw, t = n["width"] / 2, 0.14
    tl, hd = (-h / 2, -diff[1] / 2, 0.0), (h / 2, diff[1] / 2, 0.0)
    pts = []
    for (x, y, _z) in (tl, hd):
        for zz in (-hw, hw):
            pts += [Gf.Vec3f(x, y, zz), Gf.Vec3f(x, y - t, zz)]
    # vertex order per end: (-z top, -z bottom, +z top, +z bottom)
    faces = [(0, 2, 6, 4), (1, 5, 7, 3), (0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6)]
    body = define_mesh(stage, f"{path}/Body", pts, faces, n["color"])
    UsdShade.MaterialBindingAPI.Apply(body.GetPrim()).Bind(mats["ConveyorRubber"])


def author_sub(stage, root, n, mats, plc):
    path = f"{root}/SubEquipment/{sanitize(n['id'])}"
    xf = UsdGeom.Xform.Define(stage, path)
    xform_trs(xf, n["pos"], n["yaw"] if n["yaw"] else None)
    glb_exists = bool(n["glb"]) and (GLB_DIR / n["glb"]).exists()
    set_scada_attrs(xf.GetPrim(), n, plc, glb_exists)
    hx, hy, hz = n["half"]
    xf.GetPrim().CreateAttribute("archscada:halfExtents", Sdf.ValueTypeNames.Float3).Set(Gf.Vec3f(hx, hy, hz))
    body = define_box(stage, f"{path}/Body", (-hx, 0.0, -hz), (hx, 2 * hy, hz), n["color"])
    UsdShade.MaterialBindingAPI.Apply(body.GetPrim()).Bind(mats[pick_material(n)])


def author_pipe(stage, root, n, mats, plc):
    path = f"{root}/ProcessPiping/{sanitize(n['id'])}"
    pts = [Gf.Vec3d(*p) for p in n["pts"]]
    total = sum((pts[i + 1] - pts[i]).GetLength() for i in range(len(pts) - 1))
    target, acc, mid = total * 0.5, 0.0, pts[0]
    for i in range(len(pts) - 1):
        seg = (pts[i + 1] - pts[i]).GetLength()
        if acc + seg >= target or i == len(pts) - 2:
            tt = min(max((target - acc) / seg if seg > 1e-5 else 0.0, 0.0), 1.0)
            mid = pts[i] + (pts[i + 1] - pts[i]) * tt
            break
        acc += seg
    xf = UsdGeom.Xform.Define(stage, path)
    xform_trs(xf, tuple(mid))
    n["pos"] = tuple(mid)
    set_scada_attrs(xf.GetPrim(), n, plc, False)
    p = xf.GetPrim()
    S = Sdf.ValueTypeNames
    p.CreateAttribute("archscada:pipeRadius", S.Float).Set(n["radius"])
    p.CreateAttribute("archscada:flowMedium", S.String).Set(n["medium"])
    p.CreateAttribute("archscada:waypoints", S.Float3Array).Set(Vt.Vec3fArray([Gf.Vec3f(*q) for q in n["pts"]]))
    verts, faces = tube_mesh([tuple(q - mid) for q in pts], n["radius"])
    body = define_mesh(stage, f"{path}/Body", [Gf.Vec3f(*v) for v in verts], faces, n["color"])
    UsdShade.MaterialBindingAPI.Apply(body.GetPrim()).Bind(mats["ProcessWaterPiping"])


def build(out_path: Path) -> dict:
    data = parse_scene(SCENE_CS.read_text(encoding="utf-8"))
    got = {k: len(data[k]) for k in EXPECTED}
    if got != EXPECTED:
        sys.exit(f"parsed node counts {got} != expected {EXPECTED}")
    all_ids = [n["id"] for k in EXPECTED for n in data[k]]
    assert len(set(all_ids)) == 111, "duplicate interactive node ids"

    if out_path.exists():
        out_path.unlink()
    stage = Usd.Stage.CreateNew(str(out_path))
    UsdGeom.SetStageUpAxis(stage, UsdGeom.Tokens.y)
    UsdGeom.SetStageMetersPerUnit(stage, 1.0)
    root = "/ArchCoalPlant"
    top = UsdGeom.Xform.Define(stage, root)
    stage.SetDefaultPrim(top.GetPrim())
    glbs = sorted(p.name for p in GLB_DIR.glob("*.glb"))
    stage.GetRootLayer().documentation = (
        "ArchSCADA CHPP master OpenUSD digital twin. Generated by deploy/scripts/generate_openusd_plant.py "
        "from Plant3DSceneData.cs (111 interactive nodes). Do not edit by hand.")
    stage.GetRootLayer().customLayerData = {
        "archscada": {"interactiveNodes": 111, "glbAssetCount": len(glbs), "glbAssets": Vt.StringArray(glbs),
                      "generator": "deploy/scripts/generate_openusd_plant.py"}}
    top.GetPrim().CreateAttribute("archscada:interactiveNodeCount", Sdf.ValueTypeNames.Int).Set(111)
    for scope in ("PrimaryEquipment", "Conveyors", "SubEquipment", "ProcessPiping"):
        UsdGeom.Scope.Define(stage, f"{root}/{scope}")
    mats = author_materials(stage, root)
    bounds_cache: dict = {}
    for n in data["machines"]:
        author_machine(stage, root, n, mats, data["plc"], bounds_cache)
    for n in data["conveyors"]:
        author_conveyor(stage, root, n, mats, data["plc"])
    for n in data["sub"]:
        author_sub(stage, root, n, mats, data["plc"])
    for n in data["pipes"]:
        author_pipe(stage, root, n, mats, data["plc"])
    stage.GetRootLayer().Save()
    used = {n["glb"] for k in ("machines", "conveyors", "sub") for n in data[k] if n.get("glb")}
    return {"nodes": 111, "glbs_on_disk": len(glbs), "glbs_referenced": len(used), "out": str(out_path)}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--output", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()
    print(json.dumps(build(args.output), indent=2))


if __name__ == "__main__":
    main()
