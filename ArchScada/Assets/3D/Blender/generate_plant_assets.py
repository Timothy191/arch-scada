"""
Arch SCADA — Headless Blender 3D Asset Generator for Desktop OpenGL & Unity Digital Twin.
Generates beveled, multi-part industrial GLB models with authentic PBR materials for all
CHPP plant equipment.

Usage:
    blender --background --python generate_plant_assets.py -- <output_dir>

Coordinate & Orientation Convention:
    - Blender coordinates: Z-up, +X = Forward (East), +Y = Left (North), -Y = Front/Viewer (South), Z = 0 ground.
    - Exported via glTF 2.0 (`export_apply=True`, `export_yup=True`):
        glTF +X = Forward (East)
        glTF +Y = Up (ground at Y = 0)
        glTF +Z = South / Front-facing (-Y in Blender)
        glTF -Z = North (+Y in Blender)
    - All directional vehicles and process machines (wheel loader, feeders, crushers, screens)
      face +X (feed on -X, discharge/travel toward +X).
    - `wheel_loader_chassis.glb` has its boom pivot bosses at (1.30, 0.0, 1.38) in Blender
      [(1.30, 1.38, 0.0) in glTF]; `wheel_loader_bucket.glb` has origin (0, 0, 0) at that
      boom pivot pin so local Z-rotation (glTF) / Y-rotation (Blender) lifts the boom cleanly.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


# ---------------------------------------------------------------------------
# Scene & Material Helpers
# ---------------------------------------------------------------------------

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def srgb_channel_to_linear(c):
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear_rgba(hex_str, alpha=1.0):
    h = hex_str.lstrip("#")
    r = srgb_channel_to_linear(int(h[0:2], 16) / 255.0)
    g = srgb_channel_to_linear(int(h[2:4], 16) / 255.0)
    b = srgb_channel_to_linear(int(h[4:6], 16) / 255.0)
    return (r, g, b, alpha)


def make_material(name, rgba_or_hex, metallic=0.6, roughness=0.4):
    rgba = hex_to_linear_rgba(rgba_or_hex) if isinstance(rgba_or_hex, str) else rgba_or_hex
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def make_palette():
    """Authentic industrial CHPP PBR material palette."""
    return {
        "dark_steel": make_material("StructuralDarkSteel", "#242B35", metallic=0.82, roughness=0.38),
        "walkway_steel": make_material("GalvanizedWalkway", "#68737E", metallic=0.78, roughness=0.34),
        "safety_yellow": make_material("SafetyYellow", "#E09412", metallic=0.25, roughness=0.38),
        "manganese": make_material("ManganeseSteel", "#3A4048", metallic=0.86, roughness=0.32),
        "rubber": make_material("RubberTireBelt", "#16181D", metallic=0.04, roughness=0.86),
        "coal": make_material("CoalMaterial", "#1B1C20", metallic=0.08, roughness=0.78),
        "concrete": make_material("Concrete", "#5A626C", metallic=0.05, roughness=0.86),
        "tinted_glass": make_material("ControlRoomGlass", "#1E3A5F", metallic=0.18, roughness=0.08),
        "motor_blue": make_material("MotorIndustrialBlue", "#1A5C96", metallic=0.68, roughness=0.32),
        "chrome": make_material("ChromeRod", "#B8C0C8", metallic=0.92, roughness=0.18),
        "cladding": make_material("InsulatedCladding", "#BFC8D2", metallic=0.32, roughness=0.48),
        "slurry": make_material("SlurrySurface", "#143D59", metallic=0.14, roughness=0.20),
        "exhaust_dark": make_material("ExhaustSteel", "#1D2024", metallic=0.76, roughness=0.52),
    }


# ---------------------------------------------------------------------------
# Geometry Primitive & Structural Engineering Builders
# ---------------------------------------------------------------------------

def add_box(name, loc, scale, mat, rot=(0, 0, 0), bevel=0.0, segments=1):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    if bevel > 0:
        bpy.ops.object.transform_apply(scale=True)
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(30)
    obj.data.materials.append(mat)
    return obj


def add_cyl(name, loc, radius, depth, mat, verts=24, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot
    )
    obj = bpy.context.active_object
    obj.name = name
    if bevel > 0:
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = bevel
        mod.segments = 1
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(35)
    obj.data.materials.append(mat)
    return obj


def add_cone(name, loc, r1, r2, depth, mat, verts=24, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cone_add(
        vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc, rotation=rot
    )
    obj = bpy.context.active_object
    obj.name = name
    if bevel > 0:
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = bevel
        mod.segments = 1
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(35)
    obj.data.materials.append(mat)
    return obj


def add_pydata_mesh(name, verts, faces, mat, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = rot
    if bevel > 0:
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = bevel
        mod.segments = 1
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(25)
    obj.data.materials.append(mat)
    return obj


def add_frustum_box(name, x0, x1, y0, y1, z0, z1, top_dx, top_dy, mat, bevel=0.015):
    """Rectangular tapered hopper/chute from bottom (x0..x1, y0..y1, z0) to top expanded by top_dx, top_dy at z1."""
    verts = [
        (x0, y0, z0),
        (x1, y0, z0),
        (x1, y1, z0),
        (x0, y1, z0),
        (x0 - top_dx, y0 - top_dy, z1),
        (x1 + top_dx, y0 - top_dy, z1),
        (x1 + top_dx, y1 + top_dy, z1),
        (x0 - top_dx, y1 + top_dy, z1),
    ]
    faces = [
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    ]
    return add_pydata_mesh(name, verts, faces, mat, bevel=bevel)


def add_beam_between(name, p1, p2, width, height, mat, bevel=0.008):
    v1 = Vector(p1)
    v2 = Vector(p2)
    diff = v2 - v1
    length = diff.length
    if length < 1e-5:
        return None
    mid = (v1 + v2) * 0.5
    quat = diff.normalized().to_track_quat("X", "Z")
    return add_box(name, tuple(mid), (length, width, height), mat, rot=quat.to_euler(), bevel=bevel)


def add_pipe_between(name, p1, p2, radius, mat, verts=14, flange=False, mat_flange=None):
    v1 = Vector(p1)
    v2 = Vector(p2)
    diff = v2 - v1
    length = diff.length
    if length < 1e-5:
        return None
    mid = (v1 + v2) * 0.5
    quat = diff.normalized().to_track_quat("Z", "Y")
    rot = quat.to_euler()
    cyl = add_cyl(name, tuple(mid), radius, length, mat, verts=verts, rot=rot)
    if flange and mat_flange is not None:
        add_cyl(f"{name}_FlangeA", tuple(v1), radius * 1.45, 0.05, mat_flange, verts=verts, rot=rot)
        add_cyl(f"{name}_FlangeB", tuple(v2), radius * 1.45, 0.05, mat_flange, verts=verts, rot=rot)
    return cyl


def add_h_column(prefix, x, y, z0, z1, size, mat, baseplate=True):
    """Structural H-beam column with two flanges, center web, and bolted baseplate."""
    h = z1 - z0
    zc = (z0 + z1) * 0.5
    flange_t = size * 0.20
    add_box(f"{prefix}_FlangeF", (x - size * 0.4, y, zc), (flange_t, size, h), mat, bevel=0.005)
    add_box(f"{prefix}_FlangeB", (x + size * 0.4, y, zc), (flange_t, size, h), mat, bevel=0.005)
    add_box(f"{prefix}_Web", (x, y, zc), (size * 0.8, flange_t, h), mat)
    if baseplate:
        add_box(f"{prefix}_BasePlate", (x, y, z0 + 0.025), (size * 1.55, size * 1.55, 0.05), mat, bevel=0.005)


def add_x_brace(prefix, p1, p2, p3, p4, thickness, mat):
    """Cross-brace connecting corners p1-p3 and p2-p4."""
    add_beam_between(f"{prefix}_DiagA", p1, p3, thickness, thickness, mat, bevel=0.004)
    add_beam_between(f"{prefix}_DiagB", p2, p4, thickness, thickness, mat, bevel=0.004)


def add_handrail_segment(prefix, p1, p2, height, mat, posts=3):
    v1 = Vector(p1)
    v2 = Vector(p2)
    add_beam_between(f"{prefix}_TopRail", v1 + Vector((0, 0, height)), v2 + Vector((0, 0, height)), 0.045, 0.045, mat, bevel=0.005)
    add_beam_between(f"{prefix}_MidRail", v1 + Vector((0, 0, height * 0.52)), v2 + Vector((0, 0, height * 0.52)), 0.035, 0.035, mat, bevel=0.004)
    add_beam_between(f"{prefix}_ToeBoard", v1 + Vector((0, 0, 0.07)), v2 + Vector((0, 0, 0.07)), 0.025, 0.12, mat, bevel=0.003)
    for i in range(posts):
        t = i / max(1, posts - 1)
        p = v1.lerp(v2, t)
        add_box(f"{prefix}_Post_{i}", (p.x, p.y, p.z + height * 0.5), (0.045, 0.045, height), mat, bevel=0.004)


def add_walkway_deck(prefix, cx, cy, sx, sy, z_top, mat_deck, mat_rail, rails="NESW", rail_h=0.98):
    """Elevated galvanized grating walkway with perimeter safety yellow handrails."""
    deck_t = 0.09
    add_box(f"{prefix}_Deck", (cx, cy, z_top - deck_t * 0.5), (sx, sy, deck_t), mat_deck, bevel=0.01)
    x0, x1 = cx - sx * 0.5 + 0.04, cx + sx * 0.5 - 0.04
    y0, y1 = cy - sy * 0.5 + 0.04, cy + sy * 0.5 - 0.04
    if "N" in rails:
        add_handrail_segment(f"{prefix}_RailN", (x0, y1, z_top), (x1, y1, z_top), rail_h, mat_rail, posts=max(2, int(sx / 0.9) + 1))
    if "S" in rails:
        add_handrail_segment(f"{prefix}_RailS", (x0, y0, z_top), (x1, y0, z_top), rail_h, mat_rail, posts=max(2, int(sx / 0.9) + 1))
    if "E" in rails:
        add_handrail_segment(f"{prefix}_RailE", (x1, y0, z_top), (x1, y1, z_top), rail_h, mat_rail, posts=max(2, int(sy / 0.9) + 1))
    if "W" in rails:
        add_handrail_segment(f"{prefix}_RailW", (x0, y0, z_top), (x0, y1, z_top), rail_h, mat_rail, posts=max(2, int(sy / 0.9) + 1))


def add_stairs(prefix, p_bottom, p_top, width, num_steps, mat_stringer, mat_tread, mat_rail):
    """Industrial steel stairway with twin stringers, treads, and safety yellow handrails."""
    vb = Vector(p_bottom)
    vt = Vector(p_top)
    horiz = Vector((vt.x - vb.x, vt.y - vb.y, 0.0))
    if horiz.length < 1e-4:
        return
    fwd = horiz.normalized()
    side = Vector((-fwd.y, fwd.x, 0.0)) * (width * 0.5)

    add_beam_between(f"{prefix}_StringerL", vb + side, vt + side, 0.06, 0.14, mat_stringer, bevel=0.005)
    add_beam_between(f"{prefix}_StringerR", vb - side, vt - side, 0.06, 0.14, mat_stringer, bevel=0.005)

    yaw = math.atan2(fwd.y, fwd.x)
    step_depth = max(0.20, horiz.length / max(1, num_steps) * 1.05)
    for i in range(num_steps):
        t = (i + 0.5) / num_steps
        sp = vb.lerp(vt, t)
        add_box(f"{prefix}_Tread_{i}", (sp.x, sp.y, sp.z), (step_depth, width - 0.06, 0.045), mat_tread, rot=(0, 0, yaw), bevel=0.005)

    rail_h = 0.95
    for s_tag, offset in [("L", side), ("R", -side)]:
        add_beam_between(
            f"{prefix}_Handrail{s_tag}",
            vb + offset + Vector((0, 0, rail_h)),
            vt + offset + Vector((0, 0, rail_h)),
            0.045,
            0.045,
            mat_rail,
            bevel=0.005,
        )
        for j in (0.0, 0.5, 1.0):
            jp = vb.lerp(vt, j) + offset
            add_box(f"{prefix}_Post{s_tag}_{int(j*10)}", (jp.x, jp.y, jp.z + rail_h * 0.5), (0.045, 0.045, rail_h), mat_rail, bevel=0.004)


def add_caged_ladder(prefix, x, y, z0, z1, facing_axis, mat_steel, mat_yellow):
    """Vertical steel ladder with rungs and safety cage hoops."""
    h = z1 - z0
    zc = (z0 + z1) * 0.5
    nx, ny = facing_axis
    tx, ty = -ny, nx
    rail_off = 0.22
    add_box(f"{prefix}_RailA", (x + tx * rail_off, y + ty * rail_off, zc), (0.04, 0.04, h), mat_steel)
    add_box(f"{prefix}_RailB", (x - tx * rail_off, y - ty * rail_off, zc), (0.04, 0.04, h), mat_steel)

    rung_count = max(3, int(h / 0.30))
    for i in range(rung_count):
        rz = z0 + 0.22 + i * ((h - 0.28) / max(1, rung_count - 1))
        add_beam_between(
            f"{prefix}_Rung_{i}",
            (x + tx * rail_off, y + ty * rail_off, rz),
            (x - tx * rail_off, y - ty * rail_off, rz),
            0.025,
            0.025,
            mat_yellow,
            bevel=0.003,
        )

    # Safety cage hoops above z0 + 1.8m
    hoop_start = z0 + 1.8
    if z1 > hoop_start + 0.3:
        hoop_cx = x + nx * 0.36
        hoop_cy = y + ny * 0.36
        hoop_count = max(2, int((z1 - hoop_start) / 0.65) + 1)
        for k in range(hoop_count):
            hz = hoop_start + k * ((z1 - hoop_start) / max(1, hoop_count - 1))
            add_box(f"{prefix}_CageBack_{k}", (hoop_cx + nx * 0.28, hoop_cy + ny * 0.28, hz), (0.035 if abs(nx) else 0.58, 0.58 if abs(nx) else 0.035, 0.035), mat_yellow)
            add_beam_between(f"{prefix}_CageSideL_{k}", (x + tx * rail_off, y + ty * rail_off, hz), (hoop_cx + nx * 0.28 + tx * 0.28, hoop_cy + ny * 0.28 + ty * 0.28, hz), 0.03, 0.03, mat_yellow)
            add_beam_between(f"{prefix}_CageSideR_{k}", (x - tx * rail_off, y - ty * rail_off, hz), (hoop_cx + nx * 0.28 - tx * 0.28, hoop_cy + ny * 0.28 - ty * 0.28, hz), 0.03, 0.03, mat_yellow)


def add_coil_spring(prefix, x, y, z0, height, radius, mat_coil, mat_cap):
    """Heavy industrial coil spring pack with top/bottom seating plates."""
    add_box(f"{prefix}_BaseCap", (x, y, z0 + 0.03), (radius * 2.3, radius * 2.3, 0.06), mat_cap, bevel=0.006)
    add_box(f"{prefix}_TopCap", (x, y, z0 + height - 0.03), (radius * 2.3, radius * 2.3, 0.06), mat_cap, bevel=0.006)
    coils = 5
    usable = height - 0.12
    for i in range(coils):
        cz = z0 + 0.09 + i * (usable / coils)
        add_cyl(f"{prefix}_Coil_{i}", (x, y, cz), radius, usable / (coils * 1.45), mat_coil, verts=14, bevel=0.006)


def add_electric_motor(prefix, loc, radius, length, axis, mat_body, mat_dark, mat_yellow):
    """Industrial TEFC electric drive motor with cooling fins, cowl, terminal box & drive shaft."""
    x, y, z = loc
    rot = (0, math.pi / 2, 0) if axis == "X" else (math.pi / 2, 0, 0)
    add_cyl(f"{prefix}_Body", (x, y, z), radius, length, mat_body, verts=20, rot=rot, bevel=0.01)
    # Fan cowl at rear
    cowl_off = -length * 0.42
    cx = x + cowl_off if axis == "X" else x
    cy = y + cowl_off if axis == "Y" else y
    add_cyl(f"{prefix}_FanCowl", (cx, cy, z), radius * 1.06, length * 0.24, mat_dark, verts=20, rot=rot, bevel=0.008)
    # Shaft protruding at front
    sh_off = length * 0.56
    sx = x + sh_off if axis == "X" else x
    sy = y + sh_off if axis == "Y" else y
    add_cyl(f"{prefix}_Shaft", (sx, sy, z), radius * 0.24, length * 0.22, mat_yellow, verts=14, rot=rot)
    # Terminal box + base feet
    add_box(f"{prefix}_TermBox", (x, y, z + radius * 1.02), (radius * 0.75, radius * 0.75, radius * 0.45), mat_dark, bevel=0.008)
    add_box(f"{prefix}_Foot", (x, y, z - radius * 0.88), (length * 0.72 if axis == "X" else radius * 1.6, radius * 1.6 if axis == "X" else length * 0.72, 0.08), mat_dark, bevel=0.006)


def export_glb(output_dir, filename):
    path = os.path.join(output_dir, filename)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=False,
        export_apply=True,
        export_yup=True,
    )
    print(f"[ArchScada.Blender] Exported {path}")


# ---------------------------------------------------------------------------
# NEW 11 CHPP ASSETS (All directional equipment faces +X in Blender / glTF)
# ---------------------------------------------------------------------------

def build_wheel_loader_chassis(output_dir):
    """Articulated Front-End Wheel Loader Chassis facing +X, ground at Z=0, boom pivot at (1.30, 0, 1.38)."""
    clear_scene()
    p = make_palette()

    # Rear & Front Articulated Frames (Z = 0.56 .. 1.02)
    add_box("WL_RearChassis", (-1.15, 0.0, 0.74), (1.95, 1.15, 0.46), p["dark_steel"], bevel=0.03)
    add_box("WL_FrontChassis", (0.82, 0.0, 0.78), (1.30, 1.10, 0.48), p["dark_steel"], bevel=0.03)

    # Articulation center pivot hinge + twin steering hydraulic cylinders
    add_cyl("WL_ArticPinTop", (-0.08, 0.0, 1.02), 0.14, 0.24, p["safety_yellow"], verts=18)
    add_cyl("WL_ArticPinBot", (-0.08, 0.0, 0.56), 0.14, 0.24, p["safety_yellow"], verts=18)
    for s_tag, sy in [("L", 0.54), ("R", -0.54)]:
        add_pipe_between(f"WL_SteerCyl_{s_tag}", (-0.55, sy, 0.76), (0.35, sy * 0.85, 0.78), 0.055, p["dark_steel"])
        add_pipe_between(f"WL_SteerRod_{s_tag}", (-0.05, sy * 0.92, 0.77), (0.45, sy * 0.82, 0.78), 0.032, p["chrome"])

    # Heavy boom pivot towers & bosses at (1.30, +-0.52, 1.38)
    for s_tag, sy in [("L", 0.52), ("R", -0.52)]:
        add_box(f"WL_BoomTower_{s_tag}", (1.05, sy, 1.16), (0.68, 0.16, 0.62), p["safety_yellow"], bevel=0.02)
        add_cyl(f"WL_BoomPivotBoss_{s_tag}", (1.30, sy, 1.38), 0.12, 0.22, p["manganese"], verts=18, rot=(math.pi / 2, 0, 0))
        add_cyl(f"WL_TiltPivotBoss_{s_tag}", (1.05, sy * 0.65, 1.56), 0.09, 0.18, p["manganese"], verts=16, rot=(math.pi / 2, 0, 0))

    # Front & Rear Axles + Differential housings
    for tag, ax in [("Front", 1.05), ("Rear", -1.15)]:
        add_cyl(f"WL_Axle_{tag}", (ax, 0.0, 0.68), 0.13, 1.85, p["manganese"], verts=16, rot=(math.pi / 2, 0, 0))
        add_cyl(f"WL_Diff_{tag}", (ax, 0.0, 0.68), 0.24, 0.44, p["dark_steel"], verts=16, rot=(math.pi / 2, 0, 0))

    # 4 Large Heavy-Duty Off-Highway Tires (radius = 0.68m, Z center = 0.68m -> bottom touches Z=0)
    tire_r = 0.68
    tire_w = 0.48
    for idx, (wx, wy) in enumerate([(1.05, 0.92), (1.05, -0.92), (-1.15, 0.92), (-1.15, -0.92)], 1):
        add_cyl(f"WL_Tire_{idx}", (wx, wy, tire_r), tire_r - 0.03, tire_w, p["rubber"], verts=28, rot=(math.pi / 2, 0, 0), bevel=0.04)
        add_cyl(f"WL_Rim_{idx}", (wx, wy, tire_r), 0.36, tire_w + 0.03, p["safety_yellow"], verts=20, rot=(math.pi / 2, 0, 0), bevel=0.015)
        add_cyl(f"WL_Hub_{idx}", (wx, wy, tire_r), 0.17, tire_w + 0.08, p["dark_steel"], verts=14, rot=(math.pi / 2, 0, 0))
        # Deep chevron tread lugs around tire circumference
        lug_count = 14
        for li in range(lug_count):
            ang = 2.0 * math.pi * li / lug_count
            lx = wx + (tire_r - 0.015) * math.cos(ang)
            lz = tire_r + (tire_r - 0.015) * math.sin(ang)
            add_box(f"WL_Lug_{idx}_{li}", (lx, wy, lz), (0.055, tire_w * 0.94, 0.11), p["rubber"], rot=(0, -ang, 0), bevel=0.008)

    # Front fenders & LED work headlights
    for s_tag, sy in [("L", 0.92), ("R", -0.92)]:
        add_box(f"WL_FenderF_{s_tag}", (0.85, sy, 1.46), (0.92, 0.52, 0.07), p["dark_steel"], rot=(0, 0.14, 0), bevel=0.01)
        add_box(f"WL_FenderR_{s_tag}", (-1.15, sy, 1.44), (1.05, 0.52, 0.07), p["dark_steel"], bevel=0.01)
        add_box(f"WL_Headlight_{s_tag}", (1.32, sy * 0.72, 1.46), (0.10, 0.16, 0.10), p["chrome"], bevel=0.01)

    # Rear Engine Hood + Side Louvers + Exhaust Stack + Air Cleaner
    add_box("WL_EngineHood", (-1.38, 0.0, 1.48), (1.42, 1.24, 0.88), p["safety_yellow"], bevel=0.04)
    add_box("WL_RearGrille", (-2.10, 0.0, 1.44), (0.08, 1.12, 0.72), p["dark_steel"], bevel=0.01)
    for s_tag, sy in [("L", 0.63), ("R", -0.63)]:
        for li, lx in enumerate([-0.98, -1.24, -1.50, -1.76]):
            add_box(f"WL_Louver_{s_tag}_{li}", (lx, sy, 1.50), (0.16, 0.04, 0.44), p["dark_steel"], bevel=0.004)
    add_cyl("WL_ExhaustStack", (-1.45, -0.32, 2.28), 0.075, 0.82, p["exhaust_dark"], verts=16)
    add_cyl("WL_ExhaustTip", (-1.50, -0.32, 2.68), 0.085, 0.14, p["chrome"], verts=16, rot=(0, -0.35, 0))
    add_cyl("WL_AirPreCleaner", (-1.10, 0.34, 2.04), 0.13, 0.24, p["dark_steel"], verts=16)

    # Heavy Rear Counterweight
    add_box("WL_Counterweight", (-2.12, 0.0, 0.88), (0.58, 1.66, 0.64), p["dark_steel"], bevel=0.04)
    add_box("WL_RearBumperStripe", (-2.41, 0.0, 0.78), (0.06, 1.54, 0.14), p["safety_yellow"], bevel=0.01)

    # Elevated Operator Cab with 4 ROPS Pillars, Tinted Glass, Roof Canopy & Beacon
    add_box("WL_CabFloor", (-0.12, 0.0, 1.38), (1.16, 1.22, 0.14), p["dark_steel"], bevel=0.02)
    add_box("WL_CabGlassCore", (-0.08, 0.0, 2.08), (0.98, 1.06, 1.24), p["tinted_glass"], bevel=0.02)
    for idx, (px, py) in enumerate([(-0.56, -0.52), (0.38, -0.52), (-0.56, 0.52), (0.38, 0.52)], 1):
        add_box(f"WL_ROPS_Pillar_{idx}", (px, py, 2.08), (0.10, 0.10, 1.34), p["dark_steel"], bevel=0.012)
    add_box("WL_CabRoof", (-0.10, 0.0, 2.80), (1.22, 1.28, 0.14), p["safety_yellow"], bevel=0.03)
    add_box("WL_CabACPod", (-0.36, 0.0, 2.94), (0.44, 0.62, 0.14), p["dark_steel"], bevel=0.02)
    add_cyl("WL_Beacon", (0.28, 0.0, 2.96), 0.075, 0.16, p["safety_yellow"], verts=14)

    # Side cab access ladder steps
    for s_tag, sy in [("L", 0.66), ("R", -0.66)]:
        for step_i, sz in enumerate([0.62, 0.96]):
            add_box(f"WL_CabStep_{s_tag}_{step_i}", (-0.12, sy, sz), (0.36, 0.12, 0.045), p["walkway_steel"], bevel=0.005)
        add_box(f"WL_GrabRail_{s_tag}", (-0.42, sy, 1.72), (0.04, 0.04, 0.95), p["safety_yellow"])

    export_glb(output_dir, "wheel_loader_chassis.glb")


def build_wheel_loader_bucket(output_dir):
    """Wheel Loader Z-Bar Boom Arms + Rock Bucket (origin (0,0,0) at front-chassis boom pivot, facing +X)."""
    clear_scene()
    p = make_palette()

    # Twin lift arms extending from (0, +-0.52, 0) down-forward to (1.72, +-0.52, -0.95)
    for s_tag, sy in [("L", 0.52), ("R", -0.52)]:
        add_beam_between(f"WLB_LiftArm_{s_tag}", (0.0, sy, 0.0), (1.72, sy, -0.95), 0.12, 0.20, p["safety_yellow"], bevel=0.015)
        add_cyl(f"WLB_RearPin_{s_tag}", (0.0, sy, 0.0), 0.09, 0.22, p["manganese"], verts=16, rot=(math.pi / 2, 0, 0))
        add_cyl(f"WLB_BucketPin_{s_tag}", (1.72, sy, -0.95), 0.09, 0.22, p["manganese"], verts=16, rot=(math.pi / 2, 0, 0))
        # Lift cylinder beneath each arm
        add_pipe_between(f"WLB_LiftCyl_{s_tag}", (0.08, sy * 0.84, -0.34), (0.85, sy * 0.92, -0.45), 0.065, p["dark_steel"])
        add_pipe_between(f"WLB_LiftRod_{s_tag}", (0.75, sy * 0.90, -0.44), (1.12, sy, -0.62), 0.038, p["chrome"])

    # Heavy cross-tube & Z-bar tilt linkage
    add_cyl("WLB_CrossTube", (0.88, 0.0, -0.48), 0.13, 1.06, p["safety_yellow"], verts=18, rot=(math.pi / 2, 0, 0))
    add_box("WLB_ZBarBellcrank", (0.86, 0.0, -0.18), (0.14, 0.16, 0.86), p["dark_steel"], rot=(0, 0.22, 0), bevel=0.012)
    add_pipe_between("WLB_TiltCylBody", (-0.18, 0.0, 0.18), (0.48, 0.0, 0.20), 0.08, p["dark_steel"])
    add_pipe_between("WLB_TiltCylRod", (0.44, 0.0, 0.20), (0.78, 0.0, 0.22), 0.045, p["chrome"])
    add_beam_between("WLB_BucketLink", (0.94, 0.0, -0.56), (1.72, 0.0, -0.44), 0.10, 0.12, p["manganese"], bevel=0.01)

    # Heavy-duty Rock / Coal Bucket (width 2.10m along Y, hollow open cavity facing +X)
    bw_half = 1.05
    # Bucket floor plate
    add_box("WLB_BucketFloor", (2.15, 0.0, -1.24), (0.96, 2.06, 0.08), p["manganese"], bevel=0.01)
    # Sloped back wall
    add_box("WLB_BucketBack", (1.68, 0.0, -0.82), (0.09, 2.06, 0.88), p["dark_steel"], rot=(0, -0.20, 0), bevel=0.01)
    # Upper angled breastplate + spill guard
    add_box("WLB_BucketTop", (1.82, 0.0, -0.36), (0.46, 2.06, 0.08), p["dark_steel"], rot=(0, 0.35, 0), bevel=0.01)
    add_box("WLB_SpillGuard", (1.76, 0.0, -0.16), (0.06, 2.02, 0.26), p["safety_yellow"], rot=(0, -0.15, 0), bevel=0.008)
    for gi in range(5):
        gy = -0.78 + gi * 0.39
        add_box(f"WLB_SpillRib_{gi}", (1.76, gy, -0.16), (0.09, 0.05, 0.28), p["dark_steel"], rot=(0, -0.15, 0))

    # Tapered side plates + side cutters
    side_verts = [
        (1.60, -0.04, -1.26),
        (2.58, -0.04, -1.28),
        (2.02, -0.04, -0.38),
        (1.58, -0.04, -0.42),
        (1.60, 0.04, -1.26),
        (2.58, 0.04, -1.28),
        (2.02, 0.04, -0.38),
        (1.58, 0.04, -0.42),
    ]
    side_faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    for s_tag, sy in [("L", bw_half - 0.04), ("R", -(bw_half - 0.04))]:
        add_pydata_mesh(f"WLB_SideWall_{s_tag}", side_verts, side_faces, p["dark_steel"], loc=(0, sy, 0), bevel=0.01)
        add_box(f"WLB_SideCutter_{s_tag}", (2.28, sy, -0.96), (0.36, 0.10, 0.26), p["manganese"], rot=(0, 0.65, 0), bevel=0.008)

    # Thick cutting lip + 6 beveled digging teeth along +X edge
    add_box("WLB_CuttingLip", (2.60, 0.0, -1.25), (0.16, 2.10, 0.075), p["manganese"], bevel=0.01)
    tooth_verts = [
        (0.0, -0.045, -0.035), (0.0, 0.045, -0.035), (0.0, 0.045, 0.035), (0.0, -0.045, 0.035),
        (0.22, -0.020, -0.018), (0.22, 0.020, -0.018), (0.22, 0.020, 0.008), (0.22, -0.020, 0.008),
    ]
    for ti in range(6):
        ty = -0.875 + ti * 0.35
        add_pydata_mesh(f"WLB_Tooth_{ti}", tooth_verts, side_faces, p["safety_yellow"], loc=(2.66, ty, -1.25), bevel=0.006)

    export_glb(output_dir, "wheel_loader_bucket.glb")


def build_feeder_bin_bn101(output_dir):
    """BN-101: Stage 1 ROM Feeder Bin / Receiving Hopper (~3.6m x 3.4m x 3.8m) with -X loading ramp."""
    clear_scene()
    p = make_palette()

    # 4 heavy H-beam corner columns + X-bracing (Z = 0 .. 3.40m)
    col_xy = [(-1.15, -1.15), (1.15, -1.15), (-1.15, 1.15), (1.15, 1.15)]
    for idx, (cx, cy) in enumerate(col_xy, 1):
        add_h_column(f"BN101_Col_{idx}", cx, cy, 0.0, 3.40, 0.22, p["dark_steel"])

    # Side diagonal cross-bracing (+X and +Y/-Y bays)
    add_x_brace("BN101_BraceE", (1.15, -1.15, 0.15), (1.15, 1.15, 0.15), (1.15, 1.15, 1.85), (1.15, -1.15, 1.85), 0.075, p["dark_steel"])
    add_x_brace("BN101_BraceS", (-1.15, -1.15, 0.15), (1.15, -1.15, 0.15), (1.15, -1.15, 1.85), (-1.15, -1.15, 1.85), 0.075, p["dark_steel"])

    # Flared Steel Receiving Hopper (Z = 1.55 .. 3.45m) + external ribs
    add_frustum_box("BN101_HopperBody", -0.62, 0.62, -0.62, 0.62, 1.55, 3.45, 0.72, 0.72, p["manganese"], bevel=0.02)
    add_box("BN101_HopperRim", (0.0, 0.0, 3.50), (2.78, 2.78, 0.12), p["dark_steel"], bevel=0.015)
    for idx, rx in enumerate([-0.90, 0.0, 0.90]):
        add_box(f"BN101_RibY_{idx}", (rx, 1.32, 2.55), (0.09, 0.14, 1.55), p["dark_steel"], rot=(-0.34, 0, 0))
        add_box(f"BN101_RibYn_{idx}", (rx, -1.32, 2.55), (0.09, 0.14, 1.55), p["dark_steel"], rot=(0.34, 0, 0))

    # Top heavy grizzly bars spanning across hopper mouth at Z = 3.56m
    for gi in range(7):
        gy = -1.08 + gi * 0.36
        add_box(f"BN101_GrizzlyBar_{gi}", (0.0, gy, 3.58), (2.68, 0.065, 0.14), p["manganese"], bevel=0.008)
    for si, sx in enumerate([-0.80, 0.0, 0.80]):
        add_cyl(f"BN101_GrizzlyTie_{si}", (sx, 0.0, 3.58), 0.03, 2.48, p["dark_steel"], verts=10, rot=(math.pi / 2, 0, 0))

    # Bottom discharge throat (Z = 0.45 .. 1.55m)
    add_frustum_box("BN101_DischargeThroat", -0.42, 0.42, -0.42, 0.42, 0.48, 1.55, 0.20, 0.20, p["dark_steel"], bevel=0.015)
    add_box("BN101_ThroatFlange", (0.0, 0.0, 0.46), (1.02, 1.02, 0.07), p["safety_yellow"], bevel=0.008)

    # -X Concrete / Earthen Wing-Wall Loading Ramp (X = -3.45 .. -1.15, Z = 0 .. 2.40m)
    ramp_verts = [
        (-3.45, -1.45, 0.0), (-1.15, -1.45, 0.0), (-1.15, 1.45, 0.0), (-3.45, 1.45, 0.0),
        (-1.15, -1.45, 2.40), (-1.15, 1.45, 2.40),
    ]
    ramp_faces = [
        (0, 3, 2, 1),
        (0, 1, 4),
        (3, 5, 2),
        (1, 2, 5, 4),
        (0, 4, 5, 3),
    ]
    add_pydata_mesh("BN101_LoadingRamp", ramp_verts, ramp_faces, p["concrete"], bevel=0.02)
    # Wing walls flanking the ramp
    for s_tag, sy in [("L", 1.54), ("R", -1.54)]:
        add_box(f"BN101_WingWall_{s_tag}", (-1.85, sy, 1.30), (1.45, 0.22, 2.60), p["concrete"], bevel=0.02)
    # Safety yellow wheel-stop bumper beam at bin rim (X = -1.26, Z = 2.52m)
    add_box("BN101_WheelStop", (-1.28, 0.0, 2.52), (0.28, 2.86, 0.24), p["safety_yellow"], bevel=0.02)

    # Side maintenance walkway platform on +Y with stairs & yellow handrails
    add_walkway_deck("BN101_Walkway", 0.0, 1.72, 2.60, 0.92, 2.15, p["walkway_steel"], p["safety_yellow"], rails="NW")
    add_h_column("BN101_WalkPostA", -1.20, 2.10, 0.0, 2.10, 0.14, p["dark_steel"])
    add_h_column("BN101_WalkPostB", 1.20, 2.10, 0.0, 2.10, 0.14, p["dark_steel"])
    add_stairs("BN101_Stairs", (2.65, 1.72, 0.05), (1.30, 1.72, 2.15), 0.72, 9, p["dark_steel"], p["walkway_steel"], p["safety_yellow"])

    export_glb(output_dir, "feeder_bin_bn101.glb")


def build_vibro_feeder_vf101(output_dir):
    """VF-101: Stage 1/2 Scalping Vibrating Grizzly Feeder (~2.6m x 1.8m x 1.6m, feed -X -> discharge +X)."""
    clear_scene()
    p = make_palette()

    # Heavy steel sub-frame
    add_box("VF101_SubFrameL", (0.0, -0.74, 0.14), (2.40, 0.18, 0.24), p["dark_steel"], bevel=0.015)
    add_box("VF101_SubFrameR", (0.0, 0.74, 0.14), (2.40, 0.18, 0.24), p["dark_steel"], bevel=0.015)
    add_box("VF101_SubCrossF", (-1.05, 0.0, 0.14), (0.20, 1.56, 0.22), p["dark_steel"], bevel=0.015)
    add_box("VF101_SubCrossR", (1.05, 0.0, 0.14), (0.20, 1.56, 0.22), p["dark_steel"], bevel=0.015)

    # 4 Coil spring mounts
    for idx, (sx, sy, sh) in enumerate([(-0.98, -0.74, 0.44), (-0.98, 0.74, 0.44), (0.98, -0.74, 0.34), (0.98, 0.74, 0.34)], 1):
        add_coil_spring(f"VF101_Spring_{idx}", sx, sy, 0.26, sh, 0.10, p["safety_yellow"], p["dark_steel"])

    # Inclined feeder trough (8 deg decline toward +X)
    tilt = (0, 0.14, 0)
    add_box("VF101_SideL", (-0.05, -0.68, 1.06), (2.48, 0.10, 0.68), p["dark_steel"], rot=tilt, bevel=0.015)
    add_box("VF101_SideR", (-0.05, 0.68, 1.06), (2.48, 0.10, 0.68), p["dark_steel"], rot=tilt, bevel=0.015)
    add_box("VF101_BackPlate", (-1.24, 0.0, 1.24), (0.10, 1.44, 0.68), p["dark_steel"], rot=tilt, bevel=0.01)
    # Solid pan rear section + manganese wear liner
    add_box("VF101_PanFloor", (-0.42, 0.0, 0.86), (1.62, 1.26, 0.10), p["manganese"], rot=tilt, bevel=0.008)

    # Stepped grizzly bars at +X discharge end (two stepped sections)
    for gi in range(6):
        gy = -0.50 + gi * 0.20
        add_box(f"VF101_GrizzlyA_{gi}", (0.58, gy, 0.72), (0.46, 0.045, 0.09), p["manganese"], rot=tilt, bevel=0.005)
        add_box(f"VF101_GrizzlyB_{gi}", (1.02, gy, 0.56), (0.46, 0.045, 0.09), p["manganese"], rot=tilt, bevel=0.005)

    # Twin side-mounted unbalanced exciter motors
    for s_tag, sy in [("L", -0.88), ("R", 0.88)]:
        add_box(f"VF101_ExciterMount_{s_tag}", (-0.15, sy * 0.86, 0.96), (0.52, 0.16, 0.36), p["manganese"], rot=tilt, bevel=0.01)
        add_cyl(f"VF101_ExciterMotor_{s_tag}", (-0.15, sy, 0.96), 0.19, 0.38, p["motor_blue"], verts=18, rot=(math.pi / 2, 0, 0), bevel=0.01)
        add_cyl(f"VF101_ExciterWeight_{s_tag}", (-0.15, sy + (0.22 if sy > 0 else -0.22), 0.96), 0.21, 0.10, p["safety_yellow"], verts=18, rot=(math.pi / 2, 0, 0))

    # Bifurcated discharge chute (oversize nose +X and undersize bottom pan)
    add_box("VF101_OversizeNose", (1.36, 0.0, 0.44), (0.46, 1.28, 0.12), p["manganese"], rot=(0, 0.38, 0), bevel=0.01)
    add_frustum_box("VF101_UndersizePan", 0.45, 1.15, -0.52, 0.52, 0.14, 0.58, 0.18, 0.18, p["dark_steel"], bevel=0.01)

    export_glb(output_dir, "vibro_feeder_vf101.glb")


def build_jaw_crusher_jc101(output_dir):
    """JC-101: Single-Toggle Jaw Crusher Station (~3.8m x 3.4m x 3.8m).
    Deck at Z=1.8m; clear side shaft bosses at Z=2.5m (glTF Y=2.5m) and open throat for dynamic flywheels/swing jaw.
    """
    clear_scene()
    p = make_palette()

    # 6 braced H-columns supporting platform deck at Z = 1.80m
    for idx, (cx, cy) in enumerate([(-1.55, -1.35), (0.0, -1.35), (1.55, -1.35), (-1.55, 1.35), (0.0, 1.35), (1.55, 1.35)], 1):
        add_h_column(f"JC101_Col_{idx}", cx, cy, 0.0, 1.76, 0.20, p["dark_steel"])

    add_x_brace("JC101_BraceN1", (-1.55, 1.35, 0.12), (0.0, 1.35, 0.12), (0.0, 1.35, 1.70), (-1.55, 1.35, 1.70), 0.065, p["dark_steel"])
    add_x_brace("JC101_BraceN2", (0.0, 1.35, 0.12), (1.55, 1.35, 0.12), (1.55, 1.35, 1.70), (0.0, 1.35, 1.70), 0.065, p["dark_steel"])
    add_x_brace("JC101_BraceE", (1.55, -1.35, 0.12), (1.55, 1.35, 0.12), (1.55, 1.35, 1.70), (1.55, -1.35, 1.70), 0.065, p["dark_steel"])

    # Elevated structural platform deck (Z = 1.80m) + perimeter safety yellow handrails + stairs on -Y
    add_walkway_deck("JC101_Platform", 0.0, 0.0, 3.40, 2.95, 1.80, p["walkway_steel"], p["safety_yellow"], rails="NEW")
    add_handrail_segment("JC101_RailS_L", (-1.66, -1.43, 1.80), (-0.40, -1.43, 1.80), 0.98, p["safety_yellow"], posts=2)
    add_handrail_segment("JC101_RailS_R", (0.45, -1.43, 1.80), (1.66, -1.43, 1.80), 0.98, p["safety_yellow"], posts=2)
    add_stairs("JC101_Stairs", (0.02, -2.85, 0.05), (0.02, -1.45, 1.80), 0.80, 8, p["dark_steel"], p["walkway_steel"], p["safety_yellow"])

    # Ribbed cast-steel crusher main frame (open throat between Y=-0.62..+0.62, X=-0.55..+0.65, Z=1.85..3.15)
    add_box("JC101_SideCheekL", (0.05, -0.72, 2.48), (1.65, 0.18, 1.24), p["dark_steel"], bevel=0.025)
    add_box("JC101_SideCheekR", (0.05, 0.72, 2.48), (1.65, 0.18, 1.24), p["dark_steel"], bevel=0.025)
    # External stiffening ribs on side cheeks
    for ri, rx in enumerate([-0.55, -0.15, 0.25, 0.65]):
        add_box(f"JC101_RibL_{ri}", (rx, -0.83, 2.45), (0.09, 0.08, 1.12), p["manganese"], bevel=0.008)
        add_box(f"JC101_RibR_{ri}", (rx, 0.83, 2.45), (0.09, 0.08, 1.12), p["manganese"], bevel=0.008)

    # Front fixed jaw wall (+X side of throat) + corrugated manganese fixed jaw liner visible in open throat
    add_box("JC101_FrontFrameWall", (0.76, 0.0, 2.48), (0.24, 1.44, 1.24), p["dark_steel"], bevel=0.02)
    add_box("JC101_RearFrameWall", (-0.68, 0.0, 2.42), (0.20, 1.44, 1.12), p["dark_steel"], bevel=0.02)
    for ti in range(7):
        ty = -0.48 + ti * 0.16
        add_box(f"JC101_FixedJawTooth_{ti}", (0.60, ty, 2.48), (0.08, 0.075, 1.06), p["manganese"], rot=(0, -0.06, 0), bevel=0.006)

    # Toggle block & tension rod assembly at rear (-X)
    add_box("JC101_ToggleSeat", (-0.84, 0.0, 2.15), (0.22, 1.10, 0.36), p["manganese"], bevel=0.015)
    add_cyl("JC101_TensionRod", (-0.98, 0.0, 1.98), 0.045, 0.68, p["chrome"], verts=12, rot=(0, math.pi / 2, 0))
    add_cyl("JC101_TensionSpring", (-1.12, 0.0, 1.98), 0.11, 0.28, p["safety_yellow"], verts=14, rot=(0, math.pi / 2, 0))

    # Clear side eccentric shaft bosses at Z = 2.50m (glTF Y = 2.50m) for external dynamic flywheels
    for s_tag, sy in [("L", -0.88), ("R", 0.88)]:
        add_cyl(f"JC101_BearingBoss_{s_tag}", (0.0, sy, 2.50), 0.20, 0.18, p["manganese"], verts=20, rot=(math.pi / 2, 0, 0), bevel=0.01)
        add_cyl(f"JC101_ShaftStub_{s_tag}", (0.0, sy + (0.12 if sy > 0 else -0.12), 2.50), 0.085, 0.16, p["chrome"], verts=16, rot=(math.pi / 2, 0, 0))

    # V-belt motor skid + electric drive motor on rear-right platform
    add_box("JC101_MotorSkid", (-1.18, 0.82, 1.88), (0.85, 0.78, 0.14), p["dark_steel"], bevel=0.01)
    add_electric_motor("JC101_DriveMotor", (-1.20, 0.82, 2.22), 0.26, 0.62, "Y", p["motor_blue"], p["dark_steel"], p["safety_yellow"])
    add_box("JC101_BeltGuard", (-0.62, 1.05, 2.34), (1.35, 0.14, 0.52), p["safety_yellow"], rot=(0, -0.14, 0), bevel=0.02)

    # Bottom discharge chute (Z = 0.42 .. 1.76m)
    add_frustum_box("JC101_DischargeChute", -0.35, 0.55, -0.52, 0.52, 0.48, 1.76, 0.22, 0.16, p["dark_steel"], bevel=0.015)

    export_glb(output_dir, "jaw_crusher_jc101.glb")


def build_double_roll_crusher_drc101(output_dir):
    """DRC-101: Stage 4 Double Roller Crusher Station + Overhead Surge Hopper (~3.6m x 3.2m x 4.4m).
    Top opening of roll housing kept clearly visible at Z=2.1m (glTF Y=2.1m) for dynamic toothed rollers.
    """
    clear_scene()
    p = make_palette()

    # 4 main H-columns from Z=0 to platform Z=1.55m, continuing up to hopper frame Z=4.20m
    for idx, (cx, cy) in enumerate([(-1.45, -1.25), (1.45, -1.25), (-1.45, 1.25), (1.45, 1.25)], 1):
        add_h_column(f"DRC101_MainCol_{idx}", cx, cy, 0.0, 4.10, 0.20, p["dark_steel"])
    # Mid columns under platform
    add_h_column("DRC101_MidColL", 0.0, -1.25, 0.0, 1.52, 0.18, p["dark_steel"])
    add_h_column("DRC101_MidColR", 0.0, 1.25, 0.0, 1.52, 0.18, p["dark_steel"])

    # Platform deck at Z=1.55m + handrails + vertical caged ladder
    add_walkway_deck("DRC101_Platform", 0.0, 0.0, 3.25, 2.75, 1.55, p["walkway_steel"], p["safety_yellow"], rails="NESW")
    add_caged_ladder("DRC101_Ladder", -1.66, 0.0, 0.0, 3.85, (-1, 0), p["dark_steel"], p["safety_yellow"])

    # Heavy rectangular roll housing (Z = 1.58 .. 2.15m) with open top at Z=2.10m so rollers remain visible
    add_box("DRC101_RollWallF", (0.78, 0.0, 1.86), (0.14, 1.52, 0.56), p["dark_steel"], bevel=0.015)
    add_box("DRC101_RollWallB", (-0.78, 0.0, 1.86), (0.14, 1.52, 0.56), p["dark_steel"], bevel=0.015)
    add_box("DRC101_RollSideL", (0.0, -0.72, 1.86), (1.56, 0.14, 0.56), p["manganese"], bevel=0.015)
    add_box("DRC101_RollSideR", (0.0, 0.72, 1.86), (1.56, 0.14, 0.56), p["manganese"], bevel=0.015)
    # Inspection doors + tramp-iron hydraulic cylinders
    add_box("DRC101_InspectDoorL", (0.0, -0.81, 1.86), (0.84, 0.05, 0.36), p["safety_yellow"], bevel=0.008)
    add_box("DRC101_InspectDoorR", (0.0, 0.81, 1.86), (0.84, 0.05, 0.36), p["safety_yellow"], bevel=0.008)
    for sx in (-0.36, 0.36):
        add_cyl(f"DRC101_BearBossL_{sx:.2f}", (sx, -0.78, 2.02), 0.14, 0.16, p["manganese"], verts=16, rot=(math.pi / 2, 0, 0))
        add_cyl(f"DRC101_BearBossR_{sx:.2f}", (sx, 0.78, 2.02), 0.14, 0.16, p["manganese"], verts=16, rot=(math.pi / 2, 0, 0))

    # Twin planetary gearboxes + electric drive motors + safety yellow V-belt guards
    for s_tag, sx, sy in [("A", -0.45, -1.06), ("B", 0.45, 1.06)]:
        add_cyl(f"DRC101_Gearbox_{s_tag}", (sx, sy * 0.88, 1.98), 0.22, 0.28, p["manganese"], verts=18, rot=(math.pi / 2, 0, 0), bevel=0.01)
        add_electric_motor(f"DRC101_Motor_{s_tag}", (sx + (-0.42 if sx < 0 else 0.42), sy * 0.96, 1.86), 0.22, 0.56, "Y", p["motor_blue"], p["dark_steel"], p["safety_yellow"])
        add_box(f"DRC101_BeltGuard_{s_tag}", (sx + (-0.22 if sx < 0 else 0.22), sy * 1.12, 1.92), (0.64, 0.12, 0.42), p["safety_yellow"], bevel=0.015)

    # Overhead pyramidal Surge Hopper (Z = 2.85 .. 4.25m, leaving Z=2.15..2.85 observation gap!)
    add_frustum_box("DRC101_SurgeHopper", -0.58, 0.58, -0.58, 0.58, 2.85, 4.25, 0.78, 0.58, p["manganese"], bevel=0.02)
    add_box("DRC101_HopperRim", (0.0, 0.0, 4.28), (2.82, 2.42, 0.12), p["dark_steel"], bevel=0.015)
    # Upper hopper support beams connecting to corner columns
    for sz in (2.88, 4.10):
        add_beam_between(f"DRC101_TopBeamN_{sz:.1f}", (-1.45, 1.25, sz), (1.45, 1.25, sz), 0.14, 0.16, p["dark_steel"])
        add_beam_between(f"DRC101_TopBeamS_{sz:.1f}", (-1.45, -1.25, sz), (1.45, -1.25, sz), 0.14, 0.16, p["dark_steel"])
        add_beam_between(f"DRC101_TopBeamW_{sz:.1f}", (-1.45, -1.25, sz), (-1.45, 1.25, sz), 0.14, 0.16, p["dark_steel"])
        add_beam_between(f"DRC101_TopBeamE_{sz:.1f}", (1.45, -1.25, sz), (1.45, 1.25, sz), 0.14, 0.16, p["dark_steel"])

    # Bottom discharge chute (Z = 0.42 .. 1.55m)
    add_frustum_box("DRC101_DischargeChute", -0.45, 0.45, -0.45, 0.45, 0.45, 1.55, 0.25, 0.20, p["dark_steel"], bevel=0.015)

    export_glb(output_dir, "double_roll_crusher_drc101.glb")


def build_central_sizing_screen_ss101(output_dir):
    """SS-101: Stage 6 Central Multi-Deck Sizing Screen Tower (~5.0m x 3.4m x 4.6m, feed -X -> discharge +X)."""
    clear_scene()
    p = make_palette()

    # 6 H-columns + X-bracing
    for idx, (cx, cy) in enumerate([(-2.05, -1.45), (0.0, -1.45), (2.05, -1.45), (-2.05, 1.45), (0.0, 1.45), (2.05, 1.45)], 1):
        add_h_column(f"SS101_Col_{idx}", cx, cy, 0.0, 3.95, 0.20, p["dark_steel"])
    add_x_brace("SS101_BraceN1", (-2.05, 1.45, 0.15), (0.0, 1.45, 0.15), (0.0, 1.45, 1.85), (-2.05, 1.45, 1.85), 0.065, p["dark_steel"])
    add_x_brace("SS101_BraceS1", (-2.05, -1.45, 0.15), (0.0, -1.45, 0.15), (0.0, -1.45, 1.85), (-2.05, -1.45, 1.85), 0.065, p["dark_steel"])

    # Maintenance walkways on BOTH sides (Y = +-1.22m, Z = 1.95m) + yellow handrails + access stairs
    add_walkway_deck("SS101_WalkL", 0.0, -1.24, 4.45, 0.72, 1.95, p["walkway_steel"], p["safety_yellow"], rails="SEW")
    add_walkway_deck("SS101_WalkR", 0.0, 1.24, 4.45, 0.72, 1.95, p["walkway_steel"], p["safety_yellow"], rails="NEW")
    add_stairs("SS101_Stairs", (-2.25, -2.55, 0.05), (-2.25, -1.45, 1.95), 0.72, 9, p["dark_steel"], p["walkway_steel"], p["safety_yellow"])

    # Dual-inlet feed box at -X top (Z = 3.55 .. 4.50m)
    add_box("SS101_FeedBox", (-2.05, 0.0, 3.92), (0.88, 1.85, 0.76), p["dark_steel"], bevel=0.02)
    add_frustum_box("SS101_FeedInletA", -2.25, -1.80, -0.68, -0.14, 4.28, 4.58, 0.10, 0.10, p["manganese"], bevel=0.01)
    add_frustum_box("SS101_FeedInletB", -2.25, -1.80, 0.14, 0.68, 4.28, 4.58, 0.10, 0.10, p["manganese"], bevel=0.01)

    # 8 Heavy coil springs supporting screen body (4 per side)
    for idx, (sx, sz) in enumerate([(-1.45, 2.32), (-0.50, 2.08), (0.50, 1.84), (1.45, 1.60)], 1):
        add_coil_spring(f"SS101_SpringL_{idx}", sx, -0.92, sz - 0.38, 0.42, 0.10, p["safety_yellow"], p["dark_steel"])
        add_coil_spring(f"SS101_SpringR_{idx}", sx, 0.92, sz - 0.38, 0.42, 0.10, p["safety_yellow"], p["dark_steel"])

    # Inclined 3-deck screen body (14 deg decline toward +X)
    tilt = (0, 0.24, 0)
    add_box("SS101_SidePlateL", (0.0, -0.84, 2.55), (3.65, 0.10, 1.38), p["motor_blue"], rot=tilt, bevel=0.02)
    add_box("SS101_SidePlateR", (0.0, 0.84, 2.55), (3.65, 0.10, 1.38), p["motor_blue"], rot=tilt, bevel=0.02)
    add_box("SS101_BackPlate", (-1.75, 0.0, 2.95), (0.10, 1.68, 1.32), p["dark_steel"], rot=tilt, bevel=0.01)

    # 3 visible stepped screening decks (staggered at +X discharge)
    add_box("SS101_Deck1_Coarse", (-0.12, 0.0, 2.96), (3.35, 1.56, 0.07), p["manganese"], rot=tilt)
    add_box("SS101_Deck2_Sizing", (0.02, 0.0, 2.50), (3.55, 1.56, 0.07), p["walkway_steel"], rot=tilt)
    add_box("SS101_Deck3_Fines", (0.14, 0.0, 2.04), (3.72, 1.56, 0.07), p["dark_steel"], rot=tilt)

    # Twin overhead eccentric vibrator drive pods + cardan shaft
    add_box("SS101_DriveBridge", (0.05, 0.0, 3.50), (0.48, 1.88, 0.22), p["dark_steel"], rot=tilt, bevel=0.015)
    for s_tag, sy in [("L", -0.68), ("R", 0.68)]:
        add_cyl(f"SS101_VibPod_{s_tag}", (0.05, sy, 3.68), 0.22, 0.38, p["safety_yellow"], verts=18, rot=(math.pi / 2, 0, 0), bevel=0.01)
    add_cyl("SS101_CardanShaft", (0.05, 0.0, 3.68), 0.055, 1.56, p["chrome"], verts=14, rot=(math.pi / 2, 0, 0))
    add_electric_motor("SS101_DriveMotor", (0.05, 1.24, 3.68), 0.20, 0.48, "Y", p["motor_blue"], p["dark_steel"], p["safety_yellow"])

    # Curved oversize transfer chute (+X upper), middle plant-feed discharge lip, and bottom fines hopper
    add_box("SS101_OversizeChute", (1.98, 0.0, 2.38), (0.76, 1.52, 0.14), p["manganese"], rot=(0, 0.52, 0), bevel=0.015)
    add_box("SS101_MidFeedLip", (2.12, 0.0, 1.78), (0.72, 1.52, 0.10), p["walkway_steel"], rot=(0, 0.36, 0), bevel=0.01)
    add_frustum_box("SS101_FinesHopper", -0.55, 0.65, -0.55, 0.55, 0.38, 1.48, 1.05, 0.38, p["dark_steel"], bevel=0.02)

    export_glb(output_dir, "central_sizing_screen_ss101.glb")


def build_desliming_screen_ds201(output_dir):
    """DS-201: Wet Desliming Banana Screen (~4.4m x 3.0m x 3.8m, feed -X -> discharge +X)."""
    clear_scene()
    p = make_palette()

    # 6 braced H-columns + side walkways & handrails
    for idx, (cx, cy) in enumerate([(-1.75, -1.28), (0.0, -1.28), (1.75, -1.28), (-1.75, 1.28), (0.0, 1.28), (1.75, 1.28)], 1):
        add_h_column(f"DS201_Col_{idx}", cx, cy, 0.0, 2.45, 0.18, p["dark_steel"])
    add_walkway_deck("DS201_WalkL", 0.0, -1.10, 3.85, 0.64, 1.45, p["walkway_steel"], p["safety_yellow"], rails="SEW")
    add_walkway_deck("DS201_WalkR", 0.0, 1.10, 3.85, 0.64, 1.45, p["walkway_steel"], p["safety_yellow"], rails="NEW")
    add_caged_ladder("DS201_Ladder", -1.96, -1.10, 0.0, 2.40, (-1, 0), p["dark_steel"], p["safety_yellow"])

    # 8 Coil springs
    for idx, (sx, sz) in enumerate([(-1.35, 2.25), (-0.45, 1.86), (0.45, 1.62), (1.35, 1.48)], 1):
        add_coil_spring(f"DS201_SpringL_{idx}", sx, -0.78, sz - 0.34, 0.36, 0.09, p["safety_yellow"], p["dark_steel"])
        add_coil_spring(f"DS201_SpringR_{idx}", sx, 0.78, sz - 0.34, 0.36, 0.09, p["safety_yellow"], p["dark_steel"])

    # Multi-slope banana screen segments (3 progressive slopes: 24 deg -> 14 deg -> 5 deg)
    segs = [
        ("Seg1", -1.10, 2.38, 1.25, 0.42),
        ("Seg2", 0.02, 1.94, 1.22, 0.24),
        ("Seg3", 1.15, 1.72, 1.20, 0.09),
    ]
    for tag, sx, sz, slen, ang in segs:
        add_box(f"DS201_SideL_{tag}", (sx, -0.72, sz), (slen, 0.09, 0.72), p["motor_blue"], rot=(0, ang, 0), bevel=0.015)
        add_box(f"DS201_SideR_{tag}", (sx, 0.72, sz), (slen, 0.09, 0.72), p["motor_blue"], rot=(0, ang, 0), bevel=0.015)
        add_box(f"DS201_Deck_{tag}", (sx, 0.0, sz - 0.14), (slen, 1.36, 0.06), p["walkway_steel"], rot=(0, ang, 0))

    # Overhead vibrator beam + drive motors
    add_cyl("DS201_ExciterBeam", (-0.10, 0.0, 2.75), 0.18, 1.75, p["dark_steel"], verts=18, rot=(math.pi / 2, 0, 0))
    add_cyl("DS201_ExciterPodL", (-0.10, -0.92, 2.75), 0.24, 0.34, p["safety_yellow"], verts=18, rot=(math.pi / 2, 0, 0))
    add_cyl("DS201_ExciterPodR", (-0.10, 0.92, 2.75), 0.24, 0.34, p["safety_yellow"], verts=18, rot=(math.pi / 2, 0, 0))

    # 4 Transverse overhead spray-water manifold pipes with nozzle drops
    add_pipe_between("DS201_SprayHeader", (-1.15, 0.92, 3.02), (1.35, 0.92, 2.32), 0.055, p["motor_blue"], flange=True, mat_flange=p["dark_steel"])
    for mi, (mx, mz) in enumerate([(-0.95, 2.94), (-0.25, 2.72), (0.45, 2.52), (1.15, 2.34)], 1):
        add_pipe_between(f"DS201_SprayBar_{mi}", (mx, -0.76, mz), (mx, 0.92, mz), 0.038, p["chrome"])
        for ni in range(4):
            ny = -0.45 + ni * 0.30
            add_cone(f"DS201_Nozzle_{mi}_{ni}", (mx, ny, mz - 0.09), 0.025, 0.012, 0.10, p["safety_yellow"], verts=10, rot=(math.pi, 0, 0))

    # Feed box (-X top) & underpan slurry hopper
    add_box("DS201_FeedBox", (-1.78, 0.0, 2.95), (0.64, 1.48, 0.85), p["dark_steel"], bevel=0.02)
    add_frustum_box("DS201_SlurryUnderpan", -0.45, 0.55, -0.45, 0.45, 0.38, 1.42, 1.25, 0.40, p["dark_steel"], bevel=0.02)
    add_cyl("DS201_SlurryDrain", (0.05, 0.0, 0.24), 0.14, 0.32, p["motor_blue"], verts=14)

    export_glb(output_dir, "desliming_screen_ds201.glb")


def build_surge_bin_bn201(output_dir):
    """BN-201: Plant Feed Surge Bin (~3.0m x 3.0m x 4.6m)."""
    clear_scene()
    p = make_palette()

    # 4 braced H-beam legs (Z = 0 .. 1.95m)
    for idx, (lx, ly) in enumerate([(-1.12, -1.12), (1.12, -1.12), (-1.12, 1.12), (1.12, 1.12)], 1):
        add_h_column(f"BN201_Leg_{idx}", lx, ly, 0.0, 2.05, 0.19, p["dark_steel"])
    add_x_brace("BN201_BraceN", (-1.12, 1.12, 0.15), (1.12, 1.12, 0.15), (1.12, 1.12, 1.85), (-1.12, 1.12, 1.85), 0.06, p["dark_steel"])
    add_x_brace("BN201_BraceS", (-1.12, -1.12, 0.15), (1.12, -1.12, 0.15), (1.12, -1.12, 1.85), (-1.12, -1.12, 1.85), 0.06, p["dark_steel"])

    # Conical bottom discharge hopper + slide gate actuator
    add_cone("BN201_BottomCone", (0.0, 0.0, 1.32), 1.30, 0.36, 1.18, p["manganese"], verts=24, rot=(math.pi, 0, 0), bevel=0.015)
    add_box("BN201_SlideGate", (0.0, 0.0, 0.68), (0.86, 0.72, 0.14), p["dark_steel"], bevel=0.01)
    add_cyl("BN201_GateActuator", (0.72, 0.0, 0.68), 0.075, 0.62, p["safety_yellow"], verts=14, rot=(0, math.pi / 2, 0))
    add_cyl("BN201_GateRod", (0.38, 0.0, 0.68), 0.032, 0.36, p["chrome"], verts=12, rot=(0, math.pi / 2, 0))

    # Upper cylindrical bin body + stiffener ribs
    add_cyl("BN201_UpperBody", (0.0, 0.0, 2.92), 1.30, 2.05, p["dark_steel"], verts=24, bevel=0.02)
    for bz in (2.25, 2.95, 3.65):
        add_cyl(f"BN201_RingRib_{bz:.2f}", (0.0, 0.0, bz), 1.37, 0.09, p["walkway_steel"], verts=24)
    for ri in range(8):
        ang = 2.0 * math.pi * ri / 8.0
        add_box(f"BN201_VertRib_{ri}", (1.33 * math.cos(ang), 1.33 * math.sin(ang), 2.92), (0.08, 0.08, 1.98), p["walkway_steel"], rot=(0, 0, ang))

    # Top deck + conveyor receiving hood + roof handrail
    add_cyl("BN201_TopDeck", (0.0, 0.0, 3.98), 1.38, 0.10, p["walkway_steel"], verts=24)
    add_box("BN201_ConvHood", (-0.25, 0.0, 4.30), (1.28, 0.92, 0.58), p["manganese"], bevel=0.02)
    add_box("BN201_HoodChute", (-0.85, 0.0, 4.44), (0.52, 0.72, 0.42), p["dark_steel"], rot=(0, -0.35, 0), bevel=0.01)

    # Roof safety yellow handrails around octagonal perimeter
    r_rail = 1.32
    pts = [(r_rail * math.cos(2 * math.pi * k / 8), r_rail * math.sin(2 * math.pi * k / 8), 4.03) for k in range(8)]
    for k in range(8):
        add_handrail_segment(f"BN201_RoofRail_{k}", pts[k], pts[(k + 1) % 8], 0.95, p["safety_yellow"], posts=2)

    # Vertical caged ladder on -Y face
    add_caged_ladder("BN201_Ladder", 0.0, -1.38, 0.0, 4.03, (0, -1), p["dark_steel"], p["safety_yellow"])

    export_glb(output_dir, "surge_bin_bn201.glb")


def build_wash_plant_module_wp301(output_dir):
    """WP-301: Open-Top Dense-Medium Cyclone Wash Plant (No Roof / Single Screen Deck over Tanks).
    Layout:
      - East (+X=3.10, Y=0.50 -> world X=+2.30, Z=-8.30): Massive Mixing Box receiving DS-201 coal (CV-203),
        process water (PP-203), and magnetite (TK-301), with Cyclone Feed Pump + Top-Riser Pipe to Cyclone.
      - Ground Floor (Z=0..1.90m): Under-screen tanks tapering into conical Magnetite Drip Bucket & Reclaim Pump.
      - Middle Deck (Z=1.95..3.15m): Massive parallel Product Wash Screen (Y=-1.00) & Discard Wash Screen (Y=+0.60)
        with 5 overhead magnetite-reclaim water spray bars (30 nozzles) ahead of CV-301 / CV-302 discharges.
      - Top Level (Z=3.95..4.75m, open top / no roof): Dense-Medium Cyclone feeding clean coal overflow & discard
        underflow down to the dual wash screens below.
    """
    clear_scene()
    p = make_palette()

    # 1. Open Structural Steel Frame (Ground -> Screen Deck Z=1.95m -> Open Top Posts Z=3.20m; NO ROOF)
    col_x = [-3.30, -1.10, 1.20]
    col_y = [-1.85, 1.55]
    for cx in col_x:
        for cy in col_y:
            add_h_column(f"WP301_Col_{cx:.1f}_{cy:.1f}", cx, cy, 0.0, 3.15, 0.20, p["dark_steel"])

    # Perimeter cross-bracing between ground and screen deck (Z = 0.2..1.90m)
    add_x_brace("WP301_BraceW_S", (-3.30, -1.85, 0.20), (-1.10, -1.85, 0.20), (-1.10, -1.85, 1.88), (-3.30, -1.85, 1.88), 0.06, p["dark_steel"])
    add_x_brace("WP301_BraceE_N", (-1.10, 1.55, 0.20), (1.20, 1.55, 0.20), (1.20, 1.55, 1.88), (-1.10, 1.55, 1.88), 0.06, p["dark_steel"])

    # Concrete ground floor pad beneath tanks & mixing box
    add_box("WP301_FloorSlab", (0.20, -0.05, 0.06), (8.80, 3.80, 0.12), p["concrete"], bevel=0.015)

    # 2. Ground-Floor Under-Screen Collection Tanks + Conical Magnetite Drip Bucket & Reclaim Pump
    add_box("WP301_UnderTankProduct", (-1.20, -0.95, 1.48), (3.60, 1.38, 0.72), p["dark_steel"], bevel=0.02)
    add_box("WP301_UnderTankDiscard", (-1.20, 0.65, 1.48), (3.60, 1.38, 0.72), p["dark_steel"], bevel=0.02)
    # Conical/pyramidal Drip Bucket sump capturing washed magnetite spray rinse below both screens
    add_frustum_box("WP301_MagnetiteDripBucket", -1.65, -0.55, -0.55, 0.25, 0.30, 1.15, 1.25, 1.10, p["manganese"], bevel=0.02)
    add_box("WP301_DripBucketSlurry", (-1.10, -0.15, 1.02), (1.65, 1.15, 0.10), p["slurry"])
    add_pipe_between("WP301_DripBucketDrain", (-1.10, -0.15, 0.34), (-1.10, 1.45, 0.34), 0.11, p["motor_blue"], flange=True, mat_flange=p["dark_steel"])
    # Centrifugal Magnetite Reclaim Pump beside drip bucket pumping toward MS-301 / TK-301
    add_cyl("WP301_MagReclaimPump", (-1.10, 1.65, 0.42), 0.28, 0.34, p["motor_blue"], verts=18, rot=(0, math.pi / 2, 0), bevel=0.012)
    add_electric_motor("WP301_MagReclaimMotor", (-1.65, 1.65, 0.42), 0.20, 0.46, "X", p["motor_blue"], p["dark_steel"], p["safety_yellow"])
    add_pipe_between("WP301_MagReclaimOut", (-1.10, 1.72, 0.68), (-1.10, 2.35, 1.28), 0.095, p["motor_blue"], flange=True, mat_flange=p["safety_yellow"])

    # 3. East Massive Mixing Box (Blender X=+3.10, Y=+0.50 -> world X=+2.30, Z=-8.30)
    mx, my = 3.10, 0.50
    add_box("WP301_MixingBoxBody", (mx, my, 1.08), (2.10, 2.10, 1.92), p["motor_blue"], bevel=0.025)
    add_box("WP301_MixingBoxRim", (mx, my, 2.08), (2.24, 2.24, 0.12), p["dark_steel"], bevel=0.015)
    add_frustum_box("WP301_MixingBoxFlaredTop", mx - 0.96, mx + 0.96, my - 0.96, my + 0.96, 1.85, 2.26, 0.18, 0.18, p["dark_steel"], bevel=0.015)
    add_box("WP301_MixingBoxCoalMix", (mx, my, 1.78), (1.92, 1.92, 0.10), p["coal"])
    add_box("WP301_MixingBridge", (mx, my, 2.30), (2.28, 0.28, 0.14), p["walkway_steel"], bevel=0.01)
    add_electric_motor("WP301_MixingAgitator", (mx, my, 2.60), 0.24, 0.48, "Z", p["motor_blue"], p["dark_steel"], p["safety_yellow"])
    # Side Water & Magnetite Feed Inlets into Mixing Box
    add_pipe_between("WP301_MixWaterInlet", (mx + 0.15, my + 1.55, 1.15), (mx + 0.15, my + 0.98, 1.15), 0.09, p["motor_blue"], flange=True, mat_flange=p["safety_yellow"])
    add_pipe_between("WP301_MixMagInlet", (mx - 0.25, my + 1.55, 1.45), (mx - 0.25, my + 0.98, 1.45), 0.095, p["manganese"], flange=True, mat_flange=p["safety_yellow"])
    # Cyclone Feed Pump at Mixing Box bottom + Top-Riser Pipe up above screens into Top Cyclone
    add_cyl("WP301_CycloneFeedPump", (mx, my - 1.25, 0.52), 0.34, 0.40, p["motor_blue"], verts=20, rot=(0, math.pi / 2, 0), bevel=0.015)
    add_pipe_between("WP301_PumpSuction", (mx, my - 0.95, 0.52), (mx, my - 1.22, 0.52), 0.15, p["dark_steel"], flange=True, mat_flange=p["safety_yellow"])
    add_pipe_between("WP301_CycloneRiserV", (mx, my - 1.25, 0.72), (mx, my - 1.25, 4.35), 0.16, p["motor_blue"], flange=True, mat_flange=p["dark_steel"])
    add_pipe_between("WP301_CycloneRiserH", (mx, my - 1.25, 4.35), (1.05, -0.15, 4.30), 0.16, p["motor_blue"], flange=True, mat_flange=p["dark_steel"])

    # 4. Middle Level Screen Walkway Deck (Z=1.95m, single story over tanks) & Stairs
    add_walkway_deck("WP301_ScreenDeckS", -1.10, -2.18, 4.80, 0.85, 1.95, p["walkway_steel"], p["safety_yellow"], rails="SW")
    add_walkway_deck("WP301_ScreenDeckN", -1.10, 1.88, 4.80, 0.85, 1.95, p["walkway_steel"], p["safety_yellow"], rails="NW")
    add_walkway_deck("WP301_ScreenDeckW", -3.65, -0.15, 0.90, 4.85, 1.95, p["walkway_steel"], p["safety_yellow"], rails="W")
    add_stairs("WP301_AccessStairs", (1.20, -2.25, 0.12), (-0.45, -2.25, 1.95), 0.78, 8, p["dark_steel"], p["walkway_steel"], p["safety_yellow"])

    # 5. Dual Massive Wash Screens on top of the tanks (Product Y=-1.00 / Discard Y=+0.60, Z=2.05..3.05m)
    for scr_tag, sy, disch_x in [("Product", -1.00, -3.35), ("Discard", 0.60, -3.45)]:
        for sx in (0.65, -1.10, -2.65):
            add_coil_spring(f"WP301_{scr_tag}SpringL_{sx:.1f}", sx, sy - 0.62, 1.88, 0.28, 0.08, p["safety_yellow"], p["dark_steel"])
            add_coil_spring(f"WP301_{scr_tag}SpringR_{sx:.1f}", sx, sy + 0.62, 1.88, 0.28, 0.08, p["safety_yellow"], p["dark_steel"])
        # Side plates, stainless wedge-wire screen deck & wet coal/discard bed
        add_box(f"WP301_{scr_tag}SideL", (-1.15, sy - 0.62, 2.48), (4.30, 0.09, 0.72), p["motor_blue"], rot=(0, 0.06, 0), bevel=0.015)
        add_box(f"WP301_{scr_tag}SideR", (-1.15, sy + 0.62, 2.48), (4.30, 0.09, 0.72), p["motor_blue"], rot=(0, 0.06, 0), bevel=0.015)
        add_box(f"WP301_{scr_tag}Deck", (-1.15, sy, 2.28), (4.22, 1.14, 0.06), p["walkway_steel"], rot=(0, 0.06, 0))
        add_box(f"WP301_{scr_tag}Bed", (-1.15, sy, 2.34), (4.05, 1.04, 0.07), p["coal"], rot=(0, 0.06, 0))
        add_box(f"WP301_{scr_tag}FeedBox", (0.95, sy, 2.66), (0.65, 1.24, 0.85), p["dark_steel"], bevel=0.015)
        add_box(f"WP301_{scr_tag}Lip", (disch_x, sy, 2.12), (0.45, 1.10, 0.22), p["manganese"], rot=(0, 0.22, 0), bevel=0.01)
        # Dual-motor vibrator exciter bridge on each wash screen
        add_cyl(f"WP301_{scr_tag}Exciter", (-0.95, sy, 2.86), 0.16, 1.56, p["dark_steel"], verts=16, rot=(math.pi / 2, 0, 0))
        add_cyl(f"WP301_{scr_tag}ExcPodL", (-0.95, sy - 0.82, 2.86), 0.22, 0.30, p["safety_yellow"], verts=16, rot=(math.pi / 2, 0, 0))
        add_cyl(f"WP301_{scr_tag}ExcPodR", (-0.95, sy + 0.82, 2.86), 0.22, 0.30, p["safety_yellow"], verts=16, rot=(math.pi / 2, 0, 0))

    # 6. Overhead Magnetite-Reclaim Water Sprayers spanning BOTH screens ahead of discharges
    add_pipe_between("WP301_SprayMainHeader", (0.20, 1.48, 3.16), (-2.80, 1.48, 3.00), 0.065, p["motor_blue"], flange=True, mat_flange=p["dark_steel"])
    for mi, mx_bar in enumerate([-0.15, -0.75, -1.35, -1.95, -2.55], 1):
        bar_z = 3.12 - mi * 0.03
        add_pipe_between(f"WP301_SprayBar_{mi}", (mx_bar, -1.68, bar_z), (mx_bar, 1.48, bar_z), 0.042, p["chrome"])
        for sy_noz in (-1.35, -1.00, -0.65, 0.25, 0.60, 0.95):
            add_cone(f"WP301_Nozzle_{mi}_{sy_noz:.2f}", (mx_bar, sy_noz, bar_z - 0.09), 0.028, 0.012, 0.10, p["safety_yellow"], verts=10, rot=(math.pi, 0, 0))

    # 7. Open-Top Dense-Medium Cyclone Cradle & Large Cyclone Laying on Top (Z=3.95..4.75m, NO ROOF)
    for lx, ly in [(1.20, -1.15), (1.20, 0.75), (-0.70, -1.15), (-0.70, 0.75)]:
        add_h_column(f"WP301_CycCol_{lx:.1f}_{ly:.1f}", lx, ly, 1.95, 3.92, 0.14, p["dark_steel"], baseplate=False)
    add_box("WP301_CycCradleS", (0.25, -1.15, 3.88), (2.10, 0.16, 0.16), p["dark_steel"], bevel=0.01)
    add_box("WP301_CycCradleN", (0.25, 0.75, 3.88), (2.10, 0.16, 0.16), p["dark_steel"], bevel=0.01)
    add_box("WP301_CycCrossA", (0.85, -0.20, 3.88), (0.16, 2.05, 0.16), p["dark_steel"], bevel=0.01)
    add_box("WP301_CycCrossB", (-0.45, -0.20, 3.88), (0.16, 2.05, 0.16), p["dark_steel"], bevel=0.01)

    # Large inclined Dense-Medium Cyclone barrel & cone laying on top, aligned with wash screens below
    dmc_rot = (0, 1.32, 0)  # ~14 deg shallow cyclone lay angle on top cradle
    add_cyl("WP301_DMC_Body", (0.05, -0.20, 4.28), 0.54, 1.35, p["manganese"], verts=24, rot=dmc_rot, bevel=0.018)
    add_cone("WP301_DMC_Cone", (1.28, -0.20, 3.96), 0.54, 0.16, 1.25, p["manganese"], verts=24, rot=dmc_rot, bevel=0.012)
    add_cyl("WP301_DMC_FeedScroll", (0.45, -0.20, 4.22), 0.60, 0.24, p["motor_blue"], verts=24, rot=dmc_rot)
    # Tangential feed elbow from riser into DMC
    add_pipe_between("WP301_DMC_TangentialIn", (1.05, -0.15, 4.30), (0.55, 0.18, 4.28), 0.16, p["motor_blue"], flange=True, mat_flange=p["safety_yellow"])
    # Clean Coal Overflow Pipe -> Product Wash Screen feed box (Y=-1.00)
    add_pipe_between("WP301_DMC_OverflowA", (-0.65, -0.20, 4.46), (-0.85, -1.00, 4.10), 0.16, p["safety_yellow"], flange=True, mat_flange=p["dark_steel"])
    add_pipe_between("WP301_DMC_OverflowB", (-0.85, -1.00, 4.10), (0.75, -1.00, 2.95), 0.16, p["safety_yellow"], flange=True, mat_flange=p["dark_steel"])
    # Discard Sinks Underflow Pipe -> Discard Wash Screen feed box (Y=+0.60)
    add_pipe_between("WP301_DMC_UnderflowA", (1.88, -0.20, 3.80), (1.35, 0.60, 3.45), 0.13, p["dark_steel"], flange=True, mat_flange=p["safety_yellow"])
    add_pipe_between("WP301_DMC_UnderflowB", (1.35, 0.60, 3.45), (0.85, 0.60, 2.95), 0.13, p["dark_steel"], flange=True, mat_flange=p["safety_yellow"])

    export_glb(output_dir, "wash_plant_module_wp301.glb")


def build_stockpile_cone(output_dir):
    """Realistic terraced/faceted coal/discard stockpile cone (radius ~2.4m, height ~2.2m)."""
    clear_scene()
    p = make_palette()

    # Build multi-ring faceted coal pile with natural ~38-deg repose angle & bulldozer push terraces
    rings = 7
    sectors = 18
    base_r = 2.40
    peak_z = 2.20
    verts = []
    faces = []

    # Ring profile: (r_frac, z_frac, terrace_jitter)
    profile = [
        (1.00, 0.00),
        (0.84, 0.22),
        (0.66, 0.42),
        (0.56, 0.48),  # subtle bench/terrace
        (0.38, 0.72),
        (0.20, 0.90),
    ]

    for ri, (rf, zf) in enumerate(profile):
        for si in range(sectors):
            ang = 2.0 * math.pi * si / sectors
            # Deterministic organic facet variation (no random import needed)
            wobble = 1.0 + 0.06 * math.sin(ang * 3.0 + ri * 1.1) + 0.035 * math.cos(ang * 5.0 - ri * 0.7)
            r = base_r * rf * wobble
            z = peak_z * zf + (0.04 * math.sin(ang * 4.0 + ri) if ri > 0 else 0.0)
            verts.append((r * math.cos(ang), r * math.sin(ang), max(0.0, z)))

    # Apex vertex + center bottom vertex
    apex_idx = len(verts)
    verts.append((0.08, -0.05, peak_z))
    bot_idx = len(verts)
    verts.append((0.0, 0.0, 0.0))

    # Side ring quads
    for ri in range(len(profile) - 1):
        r0 = ri * sectors
        r1 = (ri + 1) * sectors
        for si in range(sectors):
            sn = (si + 1) % sectors
            faces.append((r0 + si, r0 + sn, r1 + sn, r1 + si))

    # Top cap to apex & bottom cap
    top_ring = (len(profile) - 1) * sectors
    for si in range(sectors):
        sn = (si + 1) % sectors
        faces.append((top_ring + si, top_ring + sn, apex_idx))
        faces.append((sn, si, bot_idx))

    add_pydata_mesh("Stockpile_CoalFaceted", verts, faces, p["coal"])

    export_glb(output_dir, "stockpile_cone.glb")


# ---------------------------------------------------------------------------
# PRESERVED 5 CHPP ASSETS (16 active GA industrial assets total; TK-201 enhanced)
# ---------------------------------------------------------------------------

def build_conveyor(output_dir):
    """CV-101 / CV-102: Troughed Belt Conveyor with side rails, rollers, pulleys & stanchions."""
    clear_scene()
    p = make_palette()
    frame_mat = p["dark_steel"]
    belt_mat = p["rubber"]
    roller_mat = p["walkway_steel"]
    amber = p["safety_yellow"]

    add_box("CV_Stringer_L", (0, -0.52, 0.72), (5.8, 0.09, 0.30), frame_mat, bevel=0.01)
    add_box("CV_Stringer_R", (0, 0.52, 0.72), (5.8, 0.09, 0.30), frame_mat, bevel=0.01)

    add_box("CV_Belt", (0, 0, 0.88), (5.95, 0.86, 0.08), belt_mat)
    add_box("CV_Skirt_L", (0, -0.45, 0.96), (5.6, 0.05, 0.14), amber)
    add_box("CV_Skirt_R", (0, 0.45, 0.96), (5.6, 0.05, 0.14), amber)

    add_cyl("CV_TailPulley", (-2.95, 0, 0.78), 0.20, 0.96, roller_mat, verts=20, rot=(math.pi / 2, 0, 0))
    add_cyl("CV_HeadPulley", (2.95, 0, 0.78), 0.22, 0.96, roller_mat, verts=20, rot=(math.pi / 2, 0, 0))

    for idx, rx in enumerate([-2.1, -1.05, 0.0, 1.05, 2.1], 1):
        add_cyl(f"CV_Roller_{idx}", (rx, 0, 0.78), 0.08, 0.92, roller_mat, verts=12, rot=(math.pi / 2, 0, 0))

    for idx, sx in enumerate([-2.3, 0.0, 2.3], 1):
        add_box(f"CV_LegL_{idx}", (sx, -0.48, 0.32), (0.12, 0.12, 0.64), frame_mat)
        add_box(f"CV_LegR_{idx}", (sx, 0.48, 0.32), (0.12, 0.12, 0.64), frame_mat)
        add_box(f"CV_Cross_{idx}", (sx, 0, 0.30), (0.10, 0.96, 0.10), frame_mat)

    export_glb(output_dir, "conveyor_segment.glb")


def build_thickener(output_dir):
    """TK-201: High-Rate Thickener with concrete basin, launder, feedwell, truss rake bridge & radial access walkway/stairway."""
    clear_scene()
    p = make_palette()

    # Main concrete basin + external buttress pilasters + upper launder rim
    add_cyl("TK201_Basin", (0, 0, 0.80), 2.80, 1.60, p["concrete"], verts=32)
    for bi in range(12):
        ang = 2.0 * math.pi * bi / 12.0
        add_box(f"TK201_Buttress_{bi}", (2.82 * math.cos(ang), 2.82 * math.sin(ang), 0.80), (0.18, 0.16, 1.54), p["concrete"], rot=(0, 0, ang), bevel=0.01)

    add_cyl("TK201_LaunderRim", (0, 0, 1.58), 2.95, 0.22, p["dark_steel"], verts=32)
    add_cyl("TK201_SlurryPool", (0, 0, 1.48), 2.66, 0.08, p["slurry"], verts=32)

    # Central feedwell & drive head
    add_cyl("TK201_Feedwell", (0, 0, 1.65), 0.68, 0.55, p["dark_steel"], verts=24)
    add_cyl("TK201_DriveHead", (0, 0, 2.28), 0.45, 0.52, p["safety_yellow"], verts=20, bevel=0.015)

    # Steel rake bridge deck along X + twin handrail beams
    add_box("TK201_RakeBridge", (0, 0, 1.84), (6.0, 0.72, 0.18), p["walkway_steel"], bevel=0.02)
    add_handrail_segment("TK201_BridgeRailL", (-2.95, -0.32, 1.93), (2.95, -0.32, 1.93), 0.78, p["safety_yellow"], posts=7)
    add_handrail_segment("TK201_BridgeRailR", (-2.95, 0.32, 1.93), (2.95, 0.32, 1.93), 0.78, p["safety_yellow"], posts=7)

    # Radial access walkway platform at +X rim + external stairway descending to ground Z=0
    add_walkway_deck("TK201_RadialAccessDeck", 3.45, 0.0, 1.10, 0.96, 1.92, p["walkway_steel"], p["safety_yellow"], rails="NS")
    add_h_column("TK201_AccessColA", 3.92, -0.42, 0.0, 1.86, 0.14, p["dark_steel"])
    add_h_column("TK201_AccessColB", 3.92, 0.42, 0.0, 1.86, 0.14, p["dark_steel"])
    add_stairs("TK201_AccessStairs", (3.95, -1.65, 0.05), (3.95, -0.45, 1.92), 0.72, 8, p["dark_steel"], p["walkway_steel"], p["safety_yellow"])

    export_glb(output_dir, "thickener_tk201.glb")


def build_pump(output_dir):
    """PP-201 / PP-202 / PP-203: Slurry Centrifugal Pump on skid with motor, volute & flanges."""
    clear_scene()
    p = make_palette()

    add_box("PP_Skid", (0, 0, 0.09), (1.45, 0.85, 0.18), p["dark_steel"], bevel=0.02)
    add_cyl("PP_Motor", (-0.32, 0, 0.52), 0.32, 0.68, p["motor_blue"], verts=22, rot=(0, math.pi / 2, 0), bevel=0.01)
    add_box("PP_TerminalBox", (-0.32, 0, 0.88), (0.22, 0.22, 0.14), p["dark_steel"], bevel=0.01)
    add_cyl("PP_CouplingGuard", (0.14, 0, 0.52), 0.18, 0.24, p["safety_yellow"], verts=16, rot=(0, math.pi / 2, 0))
    add_cyl("PP_Volute", (0.42, 0, 0.56), 0.40, 0.28, p["motor_blue"], verts=24, rot=(0, math.pi / 2, 0), bevel=0.015)
    add_cyl("PP_DischargePipe", (0.42, 0.18, 0.92), 0.12, 0.46, p["dark_steel"], verts=16)
    add_cyl("PP_DischargeFlange", (0.42, 0.18, 1.15), 0.20, 0.06, p["safety_yellow"], verts=16)
    add_cyl("PP_SuctionNozzle", (0.68, 0, 0.56), 0.14, 0.28, p["dark_steel"], verts=16, rot=(0, math.pi / 2, 0))

    export_glb(output_dir, "centrifugal_pump.glb")


def build_magnetite_tank(output_dir):
    """TK-301: Agitated Magnetite Medium Storage Tank with top bridge & agitator drive."""
    clear_scene()
    p = make_palette()
    tank_amber = make_material("MagnetiteTank", (0.70, 0.36, 0.08, 1.0), 0.55, 0.38)

    add_cyl("TK301_BaseRing", (0, 0, 0.10), 1.55, 0.20, p["dark_steel"], verts=28)
    add_cyl("TK301_Shell", (0, 0, 1.25), 1.42, 2.25, tank_amber, verts=28)
    add_cyl("TK301_TopRim", (0, 0, 2.38), 1.50, 0.12, p["dark_steel"], verts=28)
    add_box("TK301_AgitatorBridge", (0, 0, 2.52), (3.10, 0.45, 0.18), p["dark_steel"], bevel=0.02)
    add_cyl("TK301_Gearbox", (0, 0, 2.78), 0.32, 0.36, p["dark_steel"], verts=18)
    add_cyl("TK301_AgitatorMotor", (0, 0, 3.16), 0.24, 0.46, p["motor_blue"], verts=18)

    export_glb(output_dir, "magnetite_tank_tk301.glb")


def build_magnetic_separator(output_dir):
    """MS-301: Wet-Drum Magnetic Separator with frame, rotating drum & discharge launder."""
    clear_scene()
    p = make_palette()
    teal_steel = make_material("MagSepTeal", (0.06, 0.54, 0.52, 1.0), 0.68, 0.32)

    add_box("MS301_Base", (0, 0, 0.14), (2.0, 1.55, 0.26), p["dark_steel"], bevel=0.02)
    add_box("MS301_TankBox", (0, 0, 0.72), (1.75, 1.32, 0.92), teal_steel, bevel=0.03)
    add_cyl("MS301_WetDrum", (0, 0, 1.38), 0.62, 1.48, p["chrome"], verts=26, rot=(0, math.pi / 2, 0))
    add_cyl("MS301_DriveMotor", (-1.02, 0, 1.38), 0.24, 0.42, p["safety_yellow"], verts=18, rot=(0, math.pi / 2, 0))
    add_box("MS301_DischargeChute", (0, 0.82, 0.96), (1.52, 0.36, 0.30), p["dark_steel"], rot=(0.35, 0, 0))

    export_glb(output_dir, "magnetic_separator_ms301.glb")


def main():
    argv = sys.argv
    output_dir = argv[argv.index("--") + 1] if "--" in argv and len(argv) > argv.index("--") + 1 else "./Exported"
    os.makedirs(output_dir, exist_ok=True)

    # New 11 CHPP assets (all directional models face +X)
    build_wheel_loader_chassis(output_dir)
    build_wheel_loader_bucket(output_dir)
    build_feeder_bin_bn101(output_dir)
    build_vibro_feeder_vf101(output_dir)
    build_jaw_crusher_jc101(output_dir)
    build_double_roll_crusher_drc101(output_dir)
    build_central_sizing_screen_ss101(output_dir)
    build_desliming_screen_ds201(output_dir)
    build_surge_bin_bn201(output_dir)
    build_wash_plant_module_wp301(output_dir)
    build_stockpile_cone(output_dir)

    # Preserved & enhanced 5 CHPP assets (16 active GA models total)
    build_conveyor(output_dir)
    build_thickener(output_dir)
    build_pump(output_dir)
    build_magnetite_tank(output_dir)
    build_magnetic_separator(output_dir)


if __name__ == "__main__":
    main()
