# ArchScada Unity 3D Digital Twin Receiver

Read-only Unity 3D visualization client for **ArchScada**. It receives live JSON telemetry frames over UDP port `17850` from `ArchScada.Desktop` (`UnityBridgeService`) and updates 3D plant equipment visuals, flow particles, thermal heatmaps, alarm halos, conveyor animations, and camera presets.

> **Safety Boundary:** This Unity client is strictly **read-only**. It does not transmit commands, setpoints, or PLC writes back to `ArchScada.Desktop` or field devices.

---

## 1. Opening the Unity Project

1. Open **Unity Hub** (configured for Unity **2022.3.40f1 LTS**, see [`ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt)).
2. Click **Add → Add project from disk** and select:
   ```text
   ArchScada/ArchScada.Unity
   ```
3. Unity will resolve [`Packages/manifest.json`](Packages/manifest.json) automatically, including:
   - `com.unity.cloud.gltfast` (`.glb` runtime/editor importer)
   - `com.unity.textmeshpro`
   - `com.unity.inputsystem`
   - `com.unity.render-pipelines.universal`

---

## 2. One-Click Scene Build (`ArchScada → Build Coal Plant 3D Scene`)

Pre-exported Blender `.glb` models live in:

```text
ArchScada/Assets/3D/Exported/
├── centrifugal_pump.glb
├── centrifuge_cf501.glb
├── coal_silo_sl601.glb
├── conveyor_segment.glb
├── crusher_cr101.glb
├── flotation_column_fc401.glb
└── thickener_tk201.glb
```

To regenerate the `.glb` models from Blender at any time:

```bash
blender --background --python ArchScada/Assets/3D/Blender/generate_plant_assets.py -- ArchScada/Assets/3D/Exported
```

Inside the Unity Editor, run the one-click builder menu item:

```text
ArchScada → Build Coal Plant 3D Scene
```

[`ArchScadaSceneBuilder.cs`](Assets/Editor/ArchScadaSceneBuilder.cs) automatically:

1. Copies all 7 `.glb` models from `ArchScada/Assets/3D/Exported/` into `Assets/Models/` and imports them via `AssetDatabase`.
2. Lays out all CHPP equipment in 3D space (`CR-101`, `CV-101`, `SC-102`, `CV-102`, `SL-601`, `TK-201`, `PP-201`, `PP-202`, `PP-203`, `TK-301`, `MS-301`, `DM-301`, `FC-401`, `CF-501`).
3. Creates `AlarmHalo` indicators, `FlowParticles` systems, and rotating `animatedParts` for each equipment unit.
4. Creates `CameraRig` and the 5 `CameraPresets` anchors (`Plant Overview`, `Crushing & Conveyors`, `Thickener Circuit`, `Magnetite Prep`, `Top-Down Schematic`).
5. Creates `ScadaBridge` with [`ScadaUdpReceiver`](Assets/Scripts/ScadaUdpReceiver.cs) (UDP `17850`) and [`PlantDigitalTwinController`](Assets/Scripts/PlantDigitalTwinController.cs) wired to all equipment bindings and camera presets.
6. Saves `Assets/Scenes/CoalPlantTwin.unity` and registers it in **Build Settings**.

---

## 3. Verifying Live Telemetry

1. Start `ArchScada.Desktop` and navigate to the **3D Digital Twin** section.
2. Open `Assets/Scenes/CoalPlantTwin.unity` and press **Play** in the Unity Editor.
3. Confirm `ScadaUdpReceiver.IsReceiving` is `true` and `PacketsReceived` increments as `UnityBridgeService` publishes frames to `udp://127.0.0.1:17850`.
4. Toggle **Flow Particles**, **Thermal Heatmap**, **Alarm Halos**, **Animate Conveyors**, and **Camera Presets** in `ArchScada.Desktop` to observe immediate updates in the Unity scene.

---

## 4. Building a Standalone Player

### Option A: Unity Editor UI

1. Run **ArchScada → Build Coal Plant 3D Scene** if `Assets/Scenes/CoalPlantTwin.unity` has not been generated yet.
2. Open **File → Build Settings**.
3. Confirm `Assets/Scenes/CoalPlantTwin.unity` is checked in **Scenes In Build**.
4. Select your target platform (**Windows, Mac, Linux**), e.g., `Linux` (`x86_64`) or `Windows` (`x86_64`).
5. Click **Build** (or **Build And Run**) and choose an output directory (for example `Builds/Linux/ArchScadaTwin.x86_64`).

### Option B: Headless CLI Build

```bash
# Generate the scene in batchmode (optional if already saved)
unity-editor -batchmode -quit \
  -projectPath ArchScada/ArchScada.Unity \
  -executeMethod ArchScada.Unity.Editor.ArchScadaSceneBuilder.BuildCoalPlantScene

# Build standalone Linux x86_64 player
unity-editor -batchmode -quit \
  -projectPath ArchScada/ArchScada.Unity \
  -buildLinux64Player Builds/Linux/ArchScadaTwin.x86_64
```

Launch the built player alongside `ArchScada.Desktop`; it will automatically bind UDP port `17850` in read-only mode.
