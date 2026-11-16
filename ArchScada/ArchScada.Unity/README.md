# ArchScada Unity 3D Digital Twin Receiver

Read-only Unity 3D visualization client for **ArchScada**. It receives live JSON telemetry frames over UDP port `17850` from `ArchScada.Desktop` (`UnityBridgeService`) and updates 3D plant equipment visuals, flow particles, thermal heatmaps, alarm halos, conveyor animations, and camera presets.

> **Safety Boundary:** This Unity client is strictly **read-only**. It does not transmit commands, setpoints, or PLC writes back to `ArchScada.Desktop` or field devices.

---

## 1. Opening the Unity Project

1. Open **Unity Hub** (recommended Unity **2022.3 LTS** or **Unity 6 LTS** with URP or Built-in 3D).
2. Click **Add → Add project from disk** and select:
   ```text
   ArchScada/ArchScada.Unity
   ```
3. If opening for the first time without a `ProjectSettings/` folder, create a new 3D (URP) Unity project at `ArchScada/ArchScada.Unity` or copy `Assets/Scripts/` into your Unity project's `Assets/Scripts/` directory.
4. Ensure **glTFast** (`com.unity.cloud.gltfast`) is installed via **Window → Package Manager → + → Add package by name** so `.glb` models import cleanly.

---

## 2. Importing Blender `.glb` Models

Pre-exported plant models are located in:

```text
ArchScada/Assets/3D/Exported/
├── centrifugal_pump.glb
├── conveyor_segment.glb
├── crusher_cr101.glb
└── thickener_tk201.glb
```

To import them into Unity:

1. Copy or symlink the `.glb` files from `ArchScada/Assets/3D/Exported/` into `ArchScada/ArchScada.Unity/Assets/Models/`.
2. Wait for Unity + `glTFast` to import each `.glb` asset as a prefab.
3. Drag the imported prefabs into your main plant scene (e.g., `PlantDigitalTwin.unity`) and position them to match your plant layout:
   - `crusher_cr101.glb` → `CR-101` (Primary Jaw Crusher)
   - `thickener_tk201.glb` → `TK-201` (High-Rate Thickener)
   - `conveyor_segment.glb` → `CV-101`, `CV-102` (Conveyors)
   - `centrifugal_pump.glb` → `PP-201`, `PP-202`, `PP-203` (Slurry / Underflow Pumps)

---

## 3. Attaching `ScadaUdpReceiver` and `PlantDigitalTwinController`

1. Create an empty `GameObject` in the scene named `ScadaBridge`.
2. Attach [`ScadaUdpReceiver`](Assets/Scripts/ScadaUdpReceiver.cs):
   - **Listen Port**: `17850` (matches `UnityBridgeService` on `127.0.0.1:17850`)
   - **Auto Start On Enable**: `true`
   - **Stale Timeout Seconds**: `5.0`
3. Attach [`PlantDigitalTwinController`](Assets/Scripts/PlantDigitalTwinController.cs) to the same `ScadaBridge` object:
   - **Udp Receiver**: drag the `ScadaUdpReceiver` component.
   - **Equipment Bindings**: add an entry for each equipment ID (`CR-101`, `TK-201`, `CV-101`, `PP-201`, etc.), assigning:
     - `Status Renderers`: mesh renderers whose color/emission reflect `Running`, `Warning`, `Fault`, `Stopped`, or `ShowThermalHeatmap`.
     - `Alarm Halo Object`: child glow/ring GameObject toggled by `ShowAlarmHalos`.
     - `Flow Particles`: `ParticleSystem` instances toggled by `ShowFlowParticles`.
     - `Animated Parts`: rotating shafts, rakes, or rollers driven by `AnimateConveyors`.
   - **Camera Rig & Camera Presets**: assign `Main Camera` (or camera rig transform) and create empty `Transform` anchors for the presets published by `MainShellViewModel`:
     - `Plant Overview`
     - `Crushing & Conveyors`
     - `Thickener Circuit`
     - `Magnetite Prep`
     - `Top-Down Schematic`

---

## 4. Verifying Live Telemetry

1. Start `ArchScada.Desktop` and navigate to the **3D Digital Twin** section.
2. Press **Play** in the Unity Editor.
3. Confirm `ScadaUdpReceiver.IsReceiving` is `true` and `PacketsReceived` increments as `UnityBridgeService` publishes frames to `udp://127.0.0.1:17850`.
4. Toggle **Flow Particles**, **Thermal Heatmap**, **Alarm Halos**, **Animate Conveyors**, and **Camera Presets** in `ArchScada.Desktop` to observe immediate updates in the Unity scene.
