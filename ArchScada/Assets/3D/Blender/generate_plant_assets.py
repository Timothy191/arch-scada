"""
Arch SCADA — Headless Blender 3D Asset Generator for Desktop OpenGL & Unity Digital Twin
Generates beveled, multi-part industrial GLB models for all CHPP plant equipment.

Usage:
    blender --background --python generate_plant_assets.py -- <output_dir>
"""
import os
import sys
import math
import bpy


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def make_material(name, rgba, metallic=0.6, roughness=0.4):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def add_box(name, loc, scale, mat, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    if bevel > 0:
        bpy.ops.object.transform_apply(scale=True)
        mod = obj.modifiers.new(name="Bevel", type="BEVEL")
        mod.width = bevel
        mod.segments = 2
    obj.data.materials.append(mat)
    return obj


def add_cyl(name, loc, radius, depth, mat, verts=24, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    return obj


def add_cone(name, loc, r1, r2, depth, mat, verts=24, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(
        vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc, rotation=rot
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    return obj


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


def build_crusher(output_dir):
    """CR-101: Primary Jaw Crusher with structural frame, hopper, flywheel, motor & chute."""
    clear_scene()
    steel = make_material("IndustrialSteel", (0.22, 0.27, 0.33, 1.0), 0.78, 0.34)
    dark_steel = make_material("DarkSteel", (0.14, 0.17, 0.21, 1.0), 0.82, 0.42)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.25, 0.40)
    motor_blue = make_material("MotorBlue", (0.12, 0.40, 0.68, 1.0), 0.65, 0.35)

    # Heavy skid base & 4 support legs
    add_box("CR101_BaseSkid", (0, 0, 0.12), (2.4, 2.1, 0.24), dark_steel, bevel=0.03)
    for idx, (lx, ly) in enumerate([(-0.95, -0.82), (0.95, -0.82), (-0.95, 0.82), (0.95, 0.82)], 1):
        add_box(f"CR101_Leg_{idx}", (lx, ly, 0.65), (0.20, 0.20, 0.90), dark_steel)

    # Main crusher jaw chamber
    add_box("CR101_Chamber", (0, 0, 1.35), (2.0, 1.65, 1.25), steel, bevel=0.04)

    # Top feed hopper + rim flange
    add_cone("CR101_Hopper", (0, 0, 2.48), 1.55, 0.90, 1.05, amber, verts=4, rot=(math.pi, 0, math.pi / 4))
    add_box("CR101_HopperRim", (0, 0, 3.02), (2.25, 2.25, 0.10), dark_steel)

    # Side flywheel + drive motor
    add_cyl("CR101_Flywheel", (0, -1.02, 1.35), 0.56, 0.18, amber, verts=24, rot=(math.pi / 2, 0, 0))
    add_cyl("CR101_FlywheelHub", (0, -1.06, 1.35), 0.18, 0.24, dark_steel, verts=16, rot=(math.pi / 2, 0, 0))
    add_cyl("CR101_DriveMotor", (-0.65, 1.15, 0.95), 0.34, 0.88, motor_blue, verts=20, rot=(0, math.pi / 2, 0))
    add_box("CR101_BeltGuard", (-0.15, 0.98, 1.15), (0.95, 0.16, 0.58), amber, bevel=0.02)

    # Discharge chute
    add_box("CR101_DischargeChute", (0.85, 0, 0.58), (0.65, 0.95, 0.45), dark_steel, rot=(0, 0.35, 0))

    export_glb(output_dir, "crusher_cr101.glb")


def build_conveyor(output_dir):
    """CV-101 / CV-102: Troughed Belt Conveyor with side rails, rollers, pulleys & stanchions."""
    clear_scene()
    frame_mat = make_material("ConveyorFrame", (0.24, 0.30, 0.38, 1.0), 0.72, 0.38)
    belt_mat = make_material("RubberBelt", (0.09, 0.10, 0.13, 1.0), 0.05, 0.85)
    roller_mat = make_material("RollerSteel", (0.62, 0.67, 0.72, 1.0), 0.85, 0.25)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.25, 0.40)

    # Side stringer channels
    add_box("CV_Stringer_L", (0, -0.52, 0.72), (5.8, 0.09, 0.30), frame_mat, bevel=0.01)
    add_box("CV_Stringer_R", (0, 0.52, 0.72), (5.8, 0.09, 0.30), frame_mat, bevel=0.01)

    # Troughed rubber belt + side skirtboards
    add_box("CV_Belt", (0, 0, 0.88), (5.95, 0.86, 0.08), belt_mat)
    add_box("CV_Skirt_L", (0, -0.45, 0.96), (5.6, 0.05, 0.14), amber)
    add_box("CV_Skirt_R", (0, 0.45, 0.96), (5.6, 0.05, 0.14), amber)

    # Head & tail pulleys
    add_cyl("CV_TailPulley", (-2.95, 0, 0.78), 0.20, 0.96, roller_mat, verts=20, rot=(math.pi / 2, 0, 0))
    add_cyl("CV_HeadPulley", (2.95, 0, 0.78), 0.22, 0.96, roller_mat, verts=20, rot=(math.pi / 2, 0, 0))

    # Idler rollers along length
    for idx, rx in enumerate([-2.1, -1.05, 0.0, 1.05, 2.1], 1):
        add_cyl(f"CV_Roller_{idx}", (rx, 0, 0.78), 0.08, 0.92, roller_mat, verts=12, rot=(math.pi / 2, 0, 0))

    # Support stanchion frames
    for idx, sx in enumerate([-2.3, 0.0, 2.3], 1):
        add_box(f"CV_LegL_{idx}", (sx, -0.48, 0.32), (0.12, 0.12, 0.64), frame_mat)
        add_box(f"CV_LegR_{idx}", (sx, 0.48, 0.32), (0.12, 0.12, 0.64), frame_mat)
        add_box(f"CV_Cross_{idx}", (sx, 0, 0.30), (0.10, 0.96, 0.10), frame_mat)

    export_glb(output_dir, "conveyor_segment.glb")


def build_vibrating_screen(output_dir):
    """SC-102: Inclined Double-Deck Vibrating Screen with spring mounts & exciter motor."""
    clear_scene()
    screen_blue = make_material("ScreenBlue", (0.08, 0.46, 0.74, 1.0), 0.62, 0.36)
    dark_steel = make_material("DarkSteel", (0.16, 0.20, 0.25, 1.0), 0.80, 0.40)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.28, 0.40)
    mesh_mat = make_material("ScreenMesh", (0.50, 0.55, 0.60, 1.0), 0.85, 0.32)

    # Support sub-frame
    add_box("SC102_Base", (0, 0, 0.12), (2.1, 1.85, 0.22), dark_steel, bevel=0.02)

    # 4 corner isolation coil spring pedestals
    for idx, (sx, sy) in enumerate([(-0.82, -0.72), (0.82, -0.72), (-0.82, 0.72), (0.82, 0.72)], 1):
        add_cyl(f"SC102_Spring_{idx}", (sx, sy, 0.46), 0.13, 0.48, amber, verts=14)

    # Inclined screen side plates & deck
    tilt = (0, 0.18, 0)
    add_box("SC102_SideL", (0, -0.76, 1.15), (2.15, 0.10, 0.95), screen_blue, rot=tilt, bevel=0.02)
    add_box("SC102_SideR", (0, 0.76, 1.15), (2.15, 0.10, 0.95), screen_blue, rot=tilt, bevel=0.02)
    add_box("SC102_UpperDeck", (0, 0, 1.24), (2.05, 1.44, 0.08), mesh_mat, rot=tilt)
    add_box("SC102_LowerDeck", (0, 0, 0.88), (2.05, 1.44, 0.08), mesh_mat, rot=tilt)
    add_box("SC102_BackPlate", (-1.02, 0, 1.32), (0.12, 1.56, 0.95), screen_blue, rot=tilt)

    # Overhead vibrator exciter beam + twin eccentric housings
    add_cyl("SC102_ExciterShaft", (0.05, 0, 1.78), 0.20, 1.72, dark_steel, verts=18, rot=(math.pi / 2, 0, 0))
    add_cyl("SC102_ExciterMotor", (0.05, 1.05, 1.78), 0.28, 0.42, amber, verts=18, rot=(math.pi / 2, 0, 0))

    # Undersize funnel
    add_cone("SC102_UnderChute", (0, 0, 0.46), 0.85, 0.38, 0.52, dark_steel, verts=4, rot=(math.pi, 0, math.pi / 4))

    export_glb(output_dir, "vibrating_screen_sc102.glb")


def build_thickener(output_dir):
    """TK-201: High-Rate Thickener with concrete basin, launder, feedwell, and truss rake bridge."""
    clear_scene()
    concrete = make_material("ConcreteTank", (0.34, 0.39, 0.45, 1.0), 0.08, 0.82)
    bridge = make_material("BridgeSteel", (0.14, 0.48, 0.72, 1.0), 0.68, 0.36)
    slurry = make_material("SlurrySurface", (0.08, 0.24, 0.38, 1.0), 0.15, 0.22)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.25, 0.40)
    dark_steel = make_material("DarkSteel", (0.16, 0.20, 0.25, 1.0), 0.78, 0.38)

    # Main concrete basin + upper launder rim
    add_cyl("TK201_Basin", (0, 0, 0.80), 2.80, 1.60, concrete, verts=32)
    add_cyl("TK201_LaunderRim", (0, 0, 1.58), 2.95, 0.20, dark_steel, verts=32)
    add_cyl("TK201_SlurryPool", (0, 0, 1.48), 2.66, 0.08, slurry, verts=32)

    # Central feedwell & drive head
    add_cyl("TK201_Feedwell", (0, 0, 1.65), 0.68, 0.55, dark_steel, verts=24)
    add_cyl("TK201_DriveHead", (0, 0, 2.28), 0.45, 0.52, amber, verts=20)

    # Steel rake bridge deck + twin handrail beams
    add_box("TK201_RakeBridge", (0, 0, 1.84), (6.0, 0.68, 0.18), bridge, bevel=0.02)
    add_box("TK201_RailL", (0, -0.32, 2.24), (5.9, 0.05, 0.06), amber)
    add_box("TK201_RailR", (0, 0.32, 2.24), (5.9, 0.05, 0.06), amber)
    for idx, px in enumerate([-2.8, -1.4, 1.4, 2.8], 1):
        add_box(f"TK201_PostL_{idx}", (px, -0.32, 2.06), (0.05, 0.05, 0.40), amber)
        add_box(f"TK201_PostR_{idx}", (px, 0.32, 2.06), (0.05, 0.05, 0.40), amber)

    export_glb(output_dir, "thickener_tk201.glb")


def build_pump(output_dir):
    """PP-201 / PP-202 / PP-203: Slurry Centrifugal Pump on skid with motor, volute & flanges."""
    clear_scene()
    pump_blue = make_material("PumpBlue", (0.10, 0.48, 0.78, 1.0), 0.60, 0.32)
    dark_steel = make_material("DarkSteel", (0.16, 0.20, 0.25, 1.0), 0.80, 0.40)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.25, 0.40)

    add_box("PP_Skid", (0, 0, 0.09), (1.45, 0.85, 0.18), dark_steel, bevel=0.02)
    add_cyl("PP_Motor", (-0.32, 0, 0.52), 0.32, 0.68, pump_blue, verts=22, rot=(0, math.pi / 2, 0))
    add_box("PP_TerminalBox", (-0.32, 0, 0.88), (0.22, 0.22, 0.14), dark_steel)
    add_cyl("PP_CouplingGuard", (0.14, 0, 0.52), 0.18, 0.24, amber, verts=16, rot=(0, math.pi / 2, 0))
    add_cyl("PP_Volute", (0.42, 0, 0.56), 0.40, 0.28, pump_blue, verts=24, rot=(0, math.pi / 2, 0))
    add_cyl("PP_DischargePipe", (0.42, 0.18, 0.92), 0.12, 0.46, dark_steel, verts=16)
    add_cyl("PP_DischargeFlange", (0.42, 0.18, 1.15), 0.20, 0.06, amber, verts=16)
    add_cyl("PP_SuctionNozzle", (0.68, 0, 0.56), 0.14, 0.28, dark_steel, verts=16, rot=(0, math.pi / 2, 0))

    export_glb(output_dir, "centrifugal_pump.glb")


def build_magnetite_tank(output_dir):
    """TK-301: Agitated Magnetite Medium Storage Tank with top bridge & agitator drive."""
    clear_scene()
    tank_amber = make_material("MagnetiteTank", (0.70, 0.36, 0.08, 1.0), 0.55, 0.38)
    dark_steel = make_material("DarkSteel", (0.18, 0.22, 0.28, 1.0), 0.78, 0.38)
    motor_blue = make_material("MotorBlue", (0.12, 0.42, 0.70, 1.0), 0.62, 0.35)

    add_cyl("TK301_BaseRing", (0, 0, 0.10), 1.55, 0.20, dark_steel, verts=28)
    add_cyl("TK301_Shell", (0, 0, 1.25), 1.42, 2.25, tank_amber, verts=28)
    add_cyl("TK301_TopRim", (0, 0, 2.38), 1.50, 0.12, dark_steel, verts=28)
    add_box("TK301_AgitatorBridge", (0, 0, 2.52), (3.10, 0.45, 0.18), dark_steel, bevel=0.02)
    add_cyl("TK301_Gearbox", (0, 0, 2.78), 0.32, 0.36, dark_steel, verts=18)
    add_cyl("TK301_AgitatorMotor", (0, 0, 3.16), 0.24, 0.46, motor_blue, verts=18)

    export_glb(output_dir, "magnetite_tank_tk301.glb")


def build_magnetic_separator(output_dir):
    """MS-301: Wet-Drum Magnetic Separator with frame, rotating drum & discharge launder."""
    clear_scene()
    teal_steel = make_material("MagSepTeal", (0.06, 0.54, 0.52, 1.0), 0.68, 0.32)
    drum_chrome = make_material("DrumStainless", (0.68, 0.74, 0.78, 1.0), 0.88, 0.22)
    dark_steel = make_material("DarkSteel", (0.16, 0.20, 0.26, 1.0), 0.80, 0.40)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.25, 0.40)

    add_box("MS301_Base", (0, 0, 0.14), (2.0, 1.55, 0.26), dark_steel, bevel=0.02)
    add_box("MS301_TankBox", (0, 0, 0.72), (1.75, 1.32, 0.92), teal_steel, bevel=0.03)
    add_cyl("MS301_WetDrum", (0, 0, 1.38), 0.62, 1.48, drum_chrome, verts=26, rot=(0, math.pi / 2, 0))
    add_cyl("MS301_DriveMotor", (-1.02, 0, 1.38), 0.24, 0.42, amber, verts=18, rot=(0, math.pi / 2, 0))
    add_box("MS301_DischargeChute", (0, 0.82, 0.96), (1.52, 0.36, 0.30), dark_steel, rot=(0.35, 0, 0))

    export_glb(output_dir, "magnetic_separator_ms301.glb")


def build_dense_medium_sump(output_dir):
    """DM-301: Dense-Medium Cyclone Sump Tank on steel legs with conical bottom."""
    clear_scene()
    sump_blue = make_material("SumpBlue", (0.06, 0.46, 0.74, 1.0), 0.64, 0.36)
    dark_steel = make_material("DarkSteel", (0.16, 0.20, 0.26, 1.0), 0.80, 0.40)
    cyan_accent = make_material("CyanRim", (0.20, 0.70, 0.92, 1.0), 0.45, 0.35)

    for idx, (lx, ly) in enumerate([(-0.62, -0.62), (0.62, -0.62), (-0.62, 0.62), (0.62, 0.62)], 1):
        add_box(f"DM301_Leg_{idx}", (lx, ly, 0.60), (0.14, 0.14, 1.20), dark_steel)

    add_cone("DM301_ConeBottom", (0, 0, 0.56), 0.78, 0.24, 0.78, dark_steel, verts=24, rot=(math.pi, 0, 0))
    add_cyl("DM301_UpperTank", (0, 0, 1.42), 0.78, 0.96, sump_blue, verts=24)
    add_cyl("DM301_TopRim", (0, 0, 1.92), 0.86, 0.10, cyan_accent, verts=24)
    add_cyl("DM301_SuctionOut", (0.45, 0, 0.26), 0.14, 0.55, dark_steel, verts=14, rot=(0, math.pi / 2, 0))

    export_glb(output_dir, "dense_medium_sump_dm301.glb")


def build_flotation_column(output_dir):
    """FC-401: Fine Coal Flotation Column with stiffening rings, sparger manifold & froth launder."""
    clear_scene()
    col_steel = make_material("FlotationSteel", (0.08, 0.46, 0.66, 1.0), 0.70, 0.34)
    launder_mat = make_material("FrothLaunder", (0.18, 0.68, 0.86, 1.0), 0.52, 0.36)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.28, 0.40)
    dark_steel = make_material("DarkSteel", (0.16, 0.20, 0.26, 1.0), 0.80, 0.40)

    add_cyl("FC401_Base", (0, 0, 0.18), 1.15, 0.36, dark_steel, verts=28)
    add_cyl("FC401_Column", (0, 0, 1.68), 0.88, 2.75, col_steel, verts=28)
    add_cyl("FC401_SpargerRing", (0, 0, 0.75), 1.04, 0.18, amber, verts=24)
    add_cyl("FC401_MidBand", (0, 0, 1.75), 0.96, 0.12, dark_steel, verts=24)
    add_cyl("FC401_FrothLaunder", (0, 0, 2.95), 1.24, 0.42, launder_mat, verts=28)
    add_cyl("FC401_FeedPipe", (0.88, 0, 2.30), 0.12, 0.70, dark_steel, verts=14, rot=(0, math.pi / 2, 0))

    export_glb(output_dir, "flotation_column_fc401.glb")


def build_centrifuge(output_dir):
    """CF-501: Dewatering Basket Centrifuge with skid, drum housing, drive motor & discharge chute."""
    clear_scene()
    skid_steel = make_material("CentrifugeSkid", (0.20, 0.24, 0.30, 1.0), 0.76, 0.38)
    drum_purple = make_material("CentrifugeDrum", (0.44, 0.22, 0.78, 1.0), 0.60, 0.32)
    motor_blue = make_material("DriveMotor", (0.14, 0.36, 0.56, 1.0), 0.65, 0.38)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.25, 0.40)

    add_box("CF501_BaseSkid", (0, 0, 0.16), (2.25, 1.35, 0.32), skid_steel, bevel=0.02)
    add_cyl("CF501_DrumHousing", (0.22, 0, 0.88), 0.58, 1.25, drum_purple, verts=26, rot=(0, math.pi / 2, 0))
    add_cyl("CF501_DriveMotor", (-0.72, 0, 0.76), 0.30, 0.64, motor_blue, verts=20, rot=(0, math.pi / 2, 0))
    add_box("CF501_BeltGuard", (-0.34, 0, 0.84), (0.22, 0.68, 0.64), amber, bevel=0.02)
    add_cone("CF501_DischargeChute", (0.88, 0, 0.46), 0.46, 0.24, 0.54, skid_steel, verts=4, rot=(math.pi, 0, math.pi / 4))

    export_glb(output_dir, "centrifuge_cf501.glb")


def build_coal_silo(output_dir):
    """SL-601: Product Coal Loadout Silo with 4 legs, conical hopper, banded cylinder & headhouse."""
    clear_scene()
    silo_green = make_material("SiloBody", (0.06, 0.52, 0.38, 1.0), 0.35, 0.55)
    dark_steel = make_material("SiloSteel", (0.18, 0.22, 0.28, 1.0), 0.75, 0.38)
    amber = make_material("SafetyAmber", (0.86, 0.54, 0.08, 1.0), 0.28, 0.40)

    for idx, (lx, ly) in enumerate([(-1.05, -1.05), (1.05, -1.05), (-1.05, 1.05), (1.05, 1.05)], 1):
        add_cyl(f"SL601_SupportLeg_{idx}", (lx, ly, 0.72), 0.13, 1.44, dark_steel, verts=12)

    add_cone("SL601_HopperCone", (0, 0, 0.88), 1.32, 0.42, 0.96, dark_steel, verts=28, rot=(math.pi, 0, 0))
    add_cyl("SL601_Cylinder", (0, 0, 2.52), 1.32, 2.40, silo_green, verts=28)
    add_cyl("SL601_BandLower", (0, 0, 1.80), 1.38, 0.10, dark_steel, verts=28)
    add_cyl("SL601_BandUpper", (0, 0, 3.15), 1.38, 0.10, dark_steel, verts=28)
    add_cone("SL601_RoofCap", (0, 0, 3.98), 1.44, 0.36, 0.56, amber, verts=28)
    add_box("SL601_Headhouse", (0, 0, 4.40), (0.65, 0.65, 0.42), dark_steel, bevel=0.02)

    export_glb(output_dir, "coal_silo_sl601.glb")


def main():
    argv = sys.argv
    output_dir = argv[argv.index("--") + 1] if "--" in argv and len(argv) > argv.index("--") + 1 else "./Exported"
    os.makedirs(output_dir, exist_ok=True)

    build_crusher(output_dir)
    build_conveyor(output_dir)
    build_vibrating_screen(output_dir)
    build_thickener(output_dir)
    build_pump(output_dir)
    build_magnetite_tank(output_dir)
    build_magnetic_separator(output_dir)
    build_dense_medium_sump(output_dir)
    build_flotation_column(output_dir)
    build_centrifuge(output_dir)
    build_coal_silo(output_dir)


if __name__ == "__main__":
    main()
