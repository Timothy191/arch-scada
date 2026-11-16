"""
Arch SCADA — Headless Blender 3D Asset Generator for Unity Digital Twin
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


def export_glb(output_dir, filename):
    path = os.path.join(output_dir, filename)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=False,
        export_apply=True,
    )
    print(f"[ArchScada.Blender] Exported {path}")


def build_crusher(output_dir):
    clear_scene()
    steel = make_material("IndustrialSteel", (0.22, 0.27, 0.33, 1.0), 0.75, 0.35)
    accent = make_material("SafetyAmber", (0.85, 0.55, 0.08, 1.0), 0.25, 0.45)

    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(0, 0, 1.0))
    body = bpy.context.active_object
    body.name = "CR101_Frame"
    body.scale = (1.3, 1.0, 1.0)
    body.data.materials.append(steel)

    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=1.4, radius2=0.8, depth=1.0, location=(0, 0, 2.4))
    hopper = bpy.context.active_object
    hopper.name = "CR101_Hopper"
    hopper.rotation_euler = (math.pi, 0, math.pi / 4)
    hopper.data.materials.append(accent)

    export_glb(output_dir, "crusher_cr101.glb")


def build_thickener(output_dir):
    clear_scene()
    concrete = make_material("ConcreteTank", (0.38, 0.42, 0.46, 1.0), 0.05, 0.85)
    bridge = make_material("BridgeSteel", (0.15, 0.45, 0.65, 1.0), 0.65, 0.4)

    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=4.0, depth=1.8, location=(0, 0, 0.9))
    tank = bpy.context.active_object
    tank.name = "TK201_Basin"
    tank.data.materials.append(concrete)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 2.05))
    walkway = bpy.context.active_object
    walkway.name = "TK201_RakeBridge"
    walkway.scale = (8.2, 0.7, 0.25)
    walkway.data.materials.append(bridge)

    export_glb(output_dir, "thickener_tk201.glb")


def build_pump(output_dir):
    clear_scene()
    pump_blue = make_material("PumpBlue", (0.12, 0.42, 0.72, 1.0), 0.5, 0.35)

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.45, depth=1.1, location=(-0.5, 0, 0.55))
    motor = bpy.context.active_object
    motor.name = "PP_Motor"
    motor.rotation_euler = (0, math.pi / 2, 0)
    motor.data.materials.append(pump_blue)

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.6, depth=0.4, location=(0.35, 0, 0.6))
    volute = bpy.context.active_object
    volute.name = "PP_Volute"
    volute.rotation_euler = (0, math.pi / 2, 0)
    volute.data.materials.append(pump_blue)

    export_glb(output_dir, "centrifugal_pump.glb")


def build_conveyor(output_dir):
    clear_scene()
    frame_mat = make_material("ConveyorFrame", (0.25, 0.30, 0.36, 1.0), 0.7, 0.4)
    belt_mat = make_material("RubberBelt", (0.08, 0.09, 0.11, 1.0), 0.0, 0.85)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.6))
    truss = bpy.context.active_object
    truss.name = "CV_Truss"
    truss.scale = (6.0, 1.1, 0.45)
    truss.data.materials.append(frame_mat)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.9))
    belt = bpy.context.active_object
    belt.name = "CV_Belt"
    belt.scale = (6.1, 0.9, 0.1)
    belt.data.materials.append(belt_mat)

    export_glb(output_dir, "conveyor_segment.glb")


def build_flotation_column(output_dir):
    clear_scene()
    column_steel = make_material("FlotationSteel", (0.14, 0.38, 0.48, 1.0), 0.7, 0.35)
    launder_mat = make_material("FrothLaunder", (0.18, 0.58, 0.72, 1.0), 0.5, 0.4)
    accent = make_material("SafetyAmber", (0.85, 0.55, 0.08, 1.0), 0.25, 0.45)

    bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=1.1, depth=0.6, location=(0, 0, 0.3))
    base = bpy.context.active_object
    base.name = "FC401_Base"
    base.data.materials.append(column_steel)

    bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=0.9, depth=5.2, location=(0, 0, 3.2))
    column = bpy.context.active_object
    column.name = "FC401_Column"
    column.data.materials.append(column_steel)

    bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=1.25, depth=0.65, location=(0, 0, 5.65))
    launder = bpy.context.active_object
    launder.name = "FC401_FrothLaunder"
    launder.data.materials.append(launder_mat)

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=1.05, depth=0.25, location=(0, 0, 1.3))
    sparger = bpy.context.active_object
    sparger.name = "FC401_SpargerRing"
    sparger.data.materials.append(accent)

    export_glb(output_dir, "flotation_column_fc401.glb")


def build_centrifuge(output_dir):
    clear_scene()
    skid_steel = make_material("CentrifugeSkid", (0.22, 0.27, 0.33, 1.0), 0.75, 0.38)
    drum_mat = make_material("CentrifugeDrum", (0.12, 0.48, 0.68, 1.0), 0.65, 0.32)
    motor_mat = make_material("DriveMotor", (0.16, 0.30, 0.45, 1.0), 0.6, 0.4)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.25))
    skid = bpy.context.active_object
    skid.name = "CF501_BaseSkid"
    skid.scale = (3.4, 1.8, 0.5)
    skid.data.materials.append(skid_steel)

    bpy.ops.mesh.primitive_cylinder_add(vertices=28, radius=0.85, depth=2.0, location=(0.25, 0, 1.15))
    drum = bpy.context.active_object
    drum.name = "CF501_DrumHousing"
    drum.rotation_euler = (0, math.pi / 2, 0)
    drum.data.materials.append(drum_mat)

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.45, depth=0.95, location=(-1.15, 0, 1.05))
    motor = bpy.context.active_object
    motor.name = "CF501_DriveMotor"
    motor.rotation_euler = (0, math.pi / 2, 0)
    motor.data.materials.append(motor_mat)

    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.65, radius2=0.35, depth=0.8, location=(1.1, 0, 0.65))
    chute = bpy.context.active_object
    chute.name = "CF501_DischargeChute"
    chute.rotation_euler = (math.pi, 0, math.pi / 4)
    chute.data.materials.append(skid_steel)

    export_glb(output_dir, "centrifuge_cf501.glb")


def build_coal_silo(output_dir):
    clear_scene()
    silo_concrete = make_material("SiloConcrete", (0.36, 0.40, 0.45, 1.0), 0.1, 0.82)
    steel_accent = make_material("SiloSteel", (0.20, 0.26, 0.32, 1.0), 0.72, 0.38)
    roof_accent = make_material("SafetyAmber", (0.85, 0.55, 0.08, 1.0), 0.25, 0.45)

    bpy.ops.mesh.primitive_cone_add(vertices=32, radius1=2.4, radius2=0.8, depth=2.0, location=(0, 0, 1.4))
    hopper = bpy.context.active_object
    hopper.name = "SL601_HopperCone"
    hopper.rotation_euler = (math.pi, 0, 0)
    hopper.data.materials.append(steel_accent)

    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=2.4, depth=5.6, location=(0, 0, 5.2))
    cylinder = bpy.context.active_object
    cylinder.name = "SL601_Cylinder"
    cylinder.data.materials.append(silo_concrete)

    bpy.ops.mesh.primitive_cone_add(vertices=32, radius1=2.55, radius2=0.6, depth=1.1, location=(0, 0, 8.55))
    roof = bpy.context.active_object
    roof.name = "SL601_RoofCap"
    roof.data.materials.append(roof_accent)

    for idx, (lx, ly) in enumerate([(-1.7, -1.7), (1.7, -1.7), (-1.7, 1.7), (1.7, 1.7)], start=1):
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.22, depth=2.4, location=(lx, ly, 1.2))
        leg = bpy.context.active_object
        leg.name = f"SL601_SupportLeg_{idx}"
        leg.data.materials.append(steel_accent)

    export_glb(output_dir, "coal_silo_sl601.glb")


def main():
    argv = sys.argv
    output_dir = argv[argv.index("--") + 1] if "--" in argv and len(argv) > argv.index("--") + 1 else "./Exported"
    os.makedirs(output_dir, exist_ok=True)

    build_crusher(output_dir)
    build_thickener(output_dir)
    build_pump(output_dir)
    build_conveyor(output_dir)
    build_flotation_column(output_dir)
    build_centrifuge(output_dir)
    build_coal_silo(output_dir)


if __name__ == "__main__":
    main()

