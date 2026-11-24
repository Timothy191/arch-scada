using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArchScada.Unity.Editor
{
    /// <summary>
    /// One-click Unity Editor builder that imports exported Blender .glb assets,
    /// lays out the Coal Handling &amp; Preparation Plant (CHPP) General Arrangement (GA)
    /// Stages 1–6 and Wash Plant 3D scene, wires ScadaUdpReceiver + PlantDigitalTwinController,
    /// and saves CoalPlantTwin.unity.
    /// </summary>
    public static class ArchScadaSceneBuilder
    {
        private const string ModelsAssetFolder = "Assets/Models";
        private const string ScenesAssetFolder = "Assets/Scenes";
        private const string SceneAssetPath = "Assets/Scenes/CoalPlantTwin.unity";

        private static readonly string[] GlbFileNames =
        {
            "stockpile_cone.glb",
            "wheel_loader_chassis.glb",
            "wheel_loader_bucket.glb",
            "feeder_bin_bn101.glb",
            "vibro_feeder_vf101.glb",
            "jaw_crusher_jc101.glb",
            "double_roll_crusher_drc101.glb",
            "central_sizing_screen_ss101.glb",
            "surge_bin_bn201.glb",
            "desliming_screen_ds201.glb",
            "wash_plant_module_wp301.glb",
            "magnetic_separator_ms301.glb",
            "magnetite_tank_tk301.glb",
            "thickener_tk201.glb",
            "centrifugal_pump.glb",
            "conveyor_segment.glb"
        };

        private struct EquipmentSpec
        {
            public string Id;
            public string ScadaId;
            public string GlbFileName;
            public Vector3 Position;
            public Vector3 EulerAngles;
            public Vector3 Scale;
            public PrimitiveType FallbackPrimitive;
            public Vector3 FallbackScale;
            public Vector3 RotationAxis;
            public float AnimationSpeed;
            public bool HasAnimatedPart;

            public EquipmentSpec(
                string id,
                string scadaId,
                string glbFileName,
                Vector3 position,
                Vector3 eulerAngles,
                Vector3 scale,
                PrimitiveType fallbackPrimitive = PrimitiveType.Cube,
                Vector3? fallbackScale = null,
                bool hasAnimatedPart = false,
                Vector3? rotationAxis = null,
                float animationSpeed = 45f)
            {
                Id = id;
                ScadaId = scadaId;
                GlbFileName = glbFileName;
                Position = position;
                EulerAngles = eulerAngles;
                Scale = scale;
                FallbackPrimitive = fallbackPrimitive;
                FallbackScale = fallbackScale ?? Vector3.one;
                HasAnimatedPart = hasAnimatedPart;
                RotationAxis = rotationAxis ?? Vector3.up;
                AnimationSpeed = animationSpeed;
            }
        }

        private static readonly EquipmentSpec[] PlantLayout =
        {
            // Stage 1 — ROM Reception
            new EquipmentSpec("SP-001", "AF-001", "stockpile_cone.glb", new Vector3(-13.8f, 0f, 16.4f), Vector3.zero, new Vector3(1.1f, 0.9f, 1.1f), PrimitiveType.Cylinder, new Vector3(4.2f, 2.5f, 4.2f)),
            new EquipmentSpec("FEL-101", "AF-001", "wheel_loader_chassis.glb", new Vector3(-13.8f, 0f, 13.4f), new Vector3(0f, 90f, 0f), Vector3.one, PrimitiveType.Cube, new Vector3(2.2f, 1.6f, 3.0f)),
            new EquipmentSpec("BN-101", "AF-001", "feeder_bin_bn101.glb", new Vector3(-13.8f, 0f, 8.8f), new Vector3(0f, -90f, 0f), Vector3.one, PrimitiveType.Cube, new Vector3(3.4f, 3.0f, 3.8f)),

            // Stage 2–3 — Initial Separation & Primary Jaw Crushing
            new EquipmentSpec("VF-101", "AF-001", "vibro_feeder_vf101.glb", new Vector3(-13.8f, 0.2f, 6.2f), new Vector3(0f, 90f, 0f), Vector3.one, PrimitiveType.Cube, new Vector3(2.2f, 1.6f, 2.8f), false, Vector3.right, 60f),
            new EquipmentSpec("JC-101", "CR-101", "jaw_crusher_jc101.glb", new Vector3(-13.8f, 0f, -0.6f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(2.8f, 3.0f, 2.6f), false, Vector3.forward, 90f),

            // Stage 4–6 — Double Roll Crushing & Central Sizing Screen
            new EquipmentSpec("DRC-101", "CR-101", "double_roll_crusher_drc101.glb", new Vector3(-7.2f, 0f, -0.6f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(2.6f, 3.2f, 2.4f), false, Vector3.forward, 90f),
            new EquipmentSpec("SS-101", "SC-102", "central_sizing_screen_ss101.glb", new Vector3(0.2f, 0f, 0.5f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(4.2f, 3.7f, 2.8f), false, Vector3.right, 60f),

            // Wash Plant Feed, Dense-Medium Module, Medium Recovery & Thickener
            new EquipmentSpec("BN-201", "SL-601", "surge_bin_bn201.glb", new Vector3(11.8f, 0f, -0.8f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(2.9f, 3.8f, 2.9f)),
            new EquipmentSpec("DS-201", "SC-102", "desliming_screen_ds201.glb", new Vector3(8.8f, 0f, -9.8f), new Vector3(0f, 180f, 0f), Vector3.one, PrimitiveType.Cube, new Vector3(3.4f, 2.8f, 2.5f), false, Vector3.right, 60f),
            new EquipmentSpec("WP-301", "DM-301", "wash_plant_module_wp301.glb", new Vector3(-0.8f, 0f, -7.8f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(6.6f, 4.6f, 4.4f)),
            new EquipmentSpec("MS-301", "MS-301", "magnetic_separator_ms301.glb", new Vector3(-1.6f, 0f, -11.4f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(1.8f, 1.8f, 1.8f), true, Vector3.right, 75f),
            new EquipmentSpec("TK-301", "TK-301", "magnetite_tank_tk301.glb", new Vector3(-5.4f, 0f, -12.0f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(2.9f, 3.4f, 2.9f)),
            new EquipmentSpec("TK-201", "TK-201", "thickener_tk201.glb", new Vector3(-1.2f, 0f, -15.8f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(5.6f, 2.4f, 5.6f), true, Vector3.up, 18f),
            new EquipmentSpec("PP-201", "PP-201", "centrifugal_pump.glb", new Vector3(-4.6f, 0f, -15.0f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(1.2f, 1.0f, 1.2f)),
            new EquipmentSpec("PP-202", "PP-202", "centrifugal_pump.glb", new Vector3(-4.6f, 0f, -16.3f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(1.2f, 1.0f, 1.2f)),
            new EquipmentSpec("PP-203", "PP-203", "centrifugal_pump.glb", new Vector3(2.2f, 0f, -15.0f), new Vector3(0f, 180f, 0f), new Vector3(0.95f, 0.95f, 0.95f), PrimitiveType.Cylinder, new Vector3(1.1f, 0.95f, 1.1f)),
            new EquipmentSpec("SP-301", "SL-601", "stockpile_cone.glb", new Vector3(-9.8f, 0f, -5.2f), Vector3.zero, new Vector3(1.05f, 0.88f, 1.05f), PrimitiveType.Cylinder, new Vector3(3.8f, 2.4f, 3.8f)),
            new EquipmentSpec("SP-302", "CV-501", "stockpile_cone.glb", new Vector3(-10.6f, 0f, -8.2f), Vector3.zero, new Vector3(0.95f, 0.78f, 0.95f), PrimitiveType.Cylinder, new Vector3(3.4f, 2.2f, 3.4f)),

            // GA Conveyors (11 procedural / segment conveyors)
            BuildConveyorSpec("CV-101", "CV-101", new Vector3(-13.8f, 0.85f, 5.4f), new Vector3(-13.8f, 3.55f, 0.8f)),
            BuildConveyorSpec("CV-102", "CV-102", new Vector3(-12.8f, 0.65f, 6.1f), new Vector3(-1.9f, 4.15f, 1.15f)),
            BuildConveyorSpec("CV-103", "CV-101", new Vector3(-12.5f, 0.75f, -0.6f), new Vector3(-8.3f, 2.75f, -0.6f)),
            BuildConveyorSpec("CV-106", "CV-102", new Vector3(1.8f, 2.55f, -1.8f), new Vector3(-7.2f, 4.35f, -1.8f)),
            BuildConveyorSpec("CV-104", "CV-101", new Vector3(-6.3f, 0.80f, -0.4f), new Vector3(-1.9f, 4.20f, 0.15f)),
            BuildConveyorSpec("CV-105", "CV-102", new Vector3(-1.2f, 0.55f, 0.5f), new Vector3(2.8f, 1.15f, 2.2f)),
            BuildConveyorSpec("CV-201", "CV-101", new Vector3(2.3f, 1.45f, 0.4f), new Vector3(10.6f, 4.25f, -0.8f)),
            BuildConveyorSpec("CV-202", "CV-101", new Vector3(11.4f, 0.85f, -1.9f), new Vector3(9.6f, 3.95f, -8.6f)),
            BuildConveyorSpec("CV-203", "DM-301", new Vector3(7.2f, 1.85f, -9.6f), new Vector3(2.6f, 3.65f, -8.1f)),
            BuildConveyorSpec("CV-301", "CV-102", new Vector3(-4.2f, 2.10f, -6.8f), new Vector3(-8.8f, 2.65f, -5.4f)),
            BuildConveyorSpec("CV-302", "CV-501", new Vector3(-4.4f, 2.05f, -8.4f), new Vector3(-9.6f, 2.55f, -8.2f))
        };

        private static EquipmentSpec BuildConveyorSpec(string id, string scadaId, Vector3 tail, Vector3 head)
        {
            Vector3 mid = (tail + head) * 0.5f;
            Vector3 delta = head - tail;
            float len = delta.magnitude;
            float yaw = Mathf.Atan2(-delta.z, delta.x) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(delta.y / Mathf.Max(0.01f, len), -1f, 1f)) * Mathf.Rad2Deg;
            return new EquipmentSpec(
                id,
                scadaId,
                "conveyor_segment.glb",
                mid,
                new Vector3(0f, yaw, pitch),
                new Vector3(Mathf.Max(0.5f, len / 6.0f), 1f, 1f),
                PrimitiveType.Cube,
                new Vector3(len, 0.45f, 0.95f));
        }

        [MenuItem("ArchScada/Build Coal Plant 3D Scene")]
        public static void BuildCoalPlantScene()
        {
            EnsureAssetFolder(ModelsAssetFolder);
            EnsureAssetFolder(ScenesAssetFolder);

            ImportExportedGlbAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            BuildEnvironment();
            List<PlantDigitalTwinController.EquipmentBinding> bindings = BuildEquipmentHierarchy();
            (Transform cameraRig, List<PlantDigitalTwinController.CameraPresetTarget> cameraPresets) = BuildCameraRigAndPresets();
            BuildScadaBridge(bindings, cameraRig, cameraPresets);

            EditorSceneManager.SaveScene(scene, SceneAssetPath);
            RegisterSceneInBuildSettings(SceneAssetPath);

            Debug.Log($"[ArchScadaSceneBuilder] Built and saved GA 3D Digital Twin scene at {SceneAssetPath} with {bindings.Count} equipment bindings.");
        }

        private static void ImportExportedGlbAssets()
        {
            string sourceDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Assets", "3D", "Exported"));
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogWarning($"[ArchScadaSceneBuilder] Exported 3D folder not found at {sourceDir}. Using fallback primitives where needed.");
                return;
            }

            string targetDir = Path.GetFullPath(Path.Combine(Application.dataPath, "Models"));
            Directory.CreateDirectory(targetDir);

            int copied = 0;
            foreach (string fileName in GlbFileNames)
            {
                string src = Path.Combine(sourceDir, fileName);
                if (!File.Exists(src))
                {
                    Debug.LogWarning($"[ArchScadaSceneBuilder] Missing expected .glb asset: {src}");
                    continue;
                }

                string dst = Path.Combine(targetDir, fileName);
                File.Copy(src, dst, overwrite: true);
                copied++;
            }

            if (copied > 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"[ArchScadaSceneBuilder] Imported {copied} .glb models into {ModelsAssetFolder}.");
            }
        }

        private static void BuildEnvironment()
        {
            var envRoot = new GameObject("Environment");

            var pad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pad.name = "PlantGroundPad";
            pad.transform.SetParent(envRoot.transform, false);
            pad.transform.position = new Vector3(-2f, 0f, -2f);
            pad.transform.localScale = new Vector3(4.2f, 1f, 4.2f);

            var padRenderer = pad.GetComponent<Renderer>();
            if (padRenderer != null)
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.11f, 0.14f, 0.18f, 1f);
                padRenderer.sharedMaterial = mat;
            }

            if (RenderSettings.sun != null)
            {
                RenderSettings.sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
                RenderSettings.sun.intensity = 1.25f;
            }
        }

        private static List<PlantDigitalTwinController.EquipmentBinding> BuildEquipmentHierarchy()
        {
            var equipmentRoot = new GameObject("PlantEquipment");
            var bindings = new List<PlantDigitalTwinController.EquipmentBinding>(PlantLayout.Length);

            foreach (EquipmentSpec spec in PlantLayout)
            {
                var node = new GameObject(spec.Id);
                node.transform.SetParent(equipmentRoot.transform, false);
                node.transform.position = spec.Position;
                node.transform.eulerAngles = spec.EulerAngles;
                node.transform.localScale = spec.Scale;

                GameObject visualRoot = InstantiateEquipmentVisual(spec, node.transform);

                Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
                GameObject halo = CreateAlarmHalo(node.transform);
                ParticleSystem flowPs = CreateFlowParticles(node.transform);

                Transform[] animatedParts = Array.Empty<Transform>();
                if (spec.HasAnimatedPart)
                {
                    animatedParts = new[] { visualRoot.transform };
                }

                bindings.Add(new PlantDigitalTwinController.EquipmentBinding
                {
                    equipmentId = spec.ScadaId,
                    statusRenderers = renderers,
                    alarmHaloObject = halo,
                    flowParticles = new[] { flowPs },
                    animatedParts = animatedParts,
                    rotationAxis = spec.RotationAxis,
                    animationSpeedDegreesPerSec = spec.AnimationSpeed
                });
            }

            return bindings;
        }

        private static GameObject InstantiateEquipmentVisual(EquipmentSpec spec, Transform parent)
        {
            if (!string.IsNullOrEmpty(spec.GlbFileName))
            {
                string assetPath = $"{ModelsAssetFolder}/{spec.GlbFileName}";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    instance.name = $"{spec.Id}_Model";
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    return instance;
                }
            }

            var primitive = GameObject.CreatePrimitive(spec.FallbackPrimitive);
            primitive.name = $"{spec.Id}_Placeholder";
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = Vector3.zero;
            primitive.transform.localScale = spec.FallbackScale;
            return primitive;
        }

        private static GameObject CreateAlarmHalo(Transform parent)
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "AlarmHalo";
            halo.transform.SetParent(parent, false);
            halo.transform.localPosition = new Vector3(0f, 2.8f, 0f);
            halo.transform.localScale = new Vector3(1.1f, 0.35f, 1.1f);

            var col = halo.GetComponent<Collider>();
            if (col != null)
            {
                UnityEngine.Object.DestroyImmediate(col);
            }

            var rend = halo.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.96f, 0.62f, 0.04f, 0.65f);
                rend.sharedMaterial = mat;
            }

            halo.SetActive(false);
            return halo;
        }

        private static ParticleSystem CreateFlowParticles(Transform parent)
        {
            var psObj = new GameObject("FlowParticles");
            psObj.transform.SetParent(parent, false);
            psObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            var ps = psObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.4f;
            main.startSpeed = 1.2f;
            main.startSize = 0.14f;
            main.startColor = new Color(0.22f, 0.74f, 0.97f, 0.85f);
            main.maxParticles = 40;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 12f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f;

            return ps;
        }

        private static (Transform, List<PlantDigitalTwinController.CameraPresetTarget>) BuildCameraRigAndPresets()
        {
            var rigObj = new GameObject("CameraRig");
            rigObj.transform.position = new Vector3(-14.0f, 18.0f, 16.0f);
            rigObj.transform.rotation = Quaternion.Euler(36f, -33f, 0f);

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                var camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCam = camObj.AddComponent<Camera>();
            }

            mainCam.transform.SetParent(rigObj.transform, false);
            mainCam.transform.localPosition = Vector3.zero;
            mainCam.transform.localRotation = Quaternion.identity;

            var presetsRoot = new GameObject("CameraPresets");
            var presets = new List<PlantDigitalTwinController.CameraPresetTarget>
            {
                CreatePreset(presetsRoot.transform, "Plant Overview", new Vector3(-14.0f, 18.0f, 16.0f), new Vector3(36f, -33f, 0f)),
                CreatePreset(presetsRoot.transform, "Top-Down GA Layout", new Vector3(-2.0f, 34.0f, -2.5f), new Vector3(88f, 0f, 0f)),
                CreatePreset(presetsRoot.transform, "Front View (South)", new Vector3(-5.5f, 8.0f, 22.0f), new Vector3(18f, 0f, 0f)),
                CreatePreset(presetsRoot.transform, "Side View (West)", new Vector3(-28.0f, 7.5f, 6.0f), new Vector3(18f, 90f, 0f)),
                CreatePreset(presetsRoot.transform, "Stage 1: Loader & Feeder", new Vector3(-20.0f, 7.0f, 17.0f), new Vector3(26f, -45f, 0f)),
                CreatePreset(presetsRoot.transform, "Stage 2–3: Jaw Crusher", new Vector3(-18.5f, 7.5f, 8.0f), new Vector3(28f, -32f, 0f)),
                CreatePreset(presetsRoot.transform, "Stage 4–5: Double Roll Crusher", new Vector3(-9.0f, 7.5f, 6.5f), new Vector3(28f, -20f, 0f)),
                CreatePreset(presetsRoot.transform, "Stage 6: Central Sizing Screen", new Vector3(-5.0f, 8.0f, 8.0f), new Vector3(30f, -28f, 0f)),
                CreatePreset(presetsRoot.transform, "Wash Plant & Thickener", new Vector3(6.5f, 12.0f, -2.0f), new Vector3(32f, 26f, 0f))
            };

            return (rigObj.transform, presets);
        }

        private static PlantDigitalTwinController.CameraPresetTarget CreatePreset(
            Transform parent,
            string presetName,
            Vector3 position,
            Vector3 eulerAngles)
        {
            var anchor = new GameObject($"Preset_{presetName}");
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = position;
            anchor.transform.eulerAngles = eulerAngles;

            return new PlantDigitalTwinController.CameraPresetTarget
            {
                presetName = presetName,
                anchorTransform = anchor.transform
            };
        }

        private static void BuildScadaBridge(
            List<PlantDigitalTwinController.EquipmentBinding> bindings,
            Transform cameraRig,
            List<PlantDigitalTwinController.CameraPresetTarget> cameraPresets)
        {
            var bridgeObj = new GameObject("ScadaBridge");
            var receiver = bridgeObj.AddComponent<ScadaUdpReceiver>();
            var controller = bridgeObj.AddComponent<PlantDigitalTwinController>();

            var so = new SerializedObject(controller);
            so.FindProperty("udpReceiver").objectReferenceValue = receiver;
            so.FindProperty("cameraRig").objectReferenceValue = cameraRig;

            SerializedProperty bindingsProp = so.FindProperty("equipmentBindings");
            bindingsProp.arraySize = bindings.Count;
            for (int i = 0; i < bindings.Count; i++)
            {
                SerializedProperty elem = bindingsProp.GetArrayElementAtIndex(i);
                PlantDigitalTwinController.EquipmentBinding b = bindings[i];
                elem.FindPropertyRelative("equipmentId").stringValue = b.equipmentId;
                elem.FindPropertyRelative("alarmHaloObject").objectReferenceValue = b.alarmHaloObject;
                elem.FindPropertyRelative("rotationAxis").vector3Value = b.rotationAxis;
                elem.FindPropertyRelative("animationSpeedDegreesPerSec").floatValue = b.animationSpeedDegreesPerSec;

                SerializedProperty renderersProp = elem.FindPropertyRelative("statusRenderers");
                renderersProp.arraySize = b.statusRenderers != null ? b.statusRenderers.Length : 0;
                for (int r = 0; r < renderersProp.arraySize; r++)
                {
                    renderersProp.GetArrayElementAtIndex(r).objectReferenceValue = b.statusRenderers[r];
                }

                SerializedProperty psProp = elem.FindPropertyRelative("flowParticles");
                psProp.arraySize = b.flowParticles != null ? b.flowParticles.Length : 0;
                for (int p = 0; p < psProp.arraySize; p++)
                {
                    psProp.GetArrayElementAtIndex(p).objectReferenceValue = b.flowParticles[p];
                }

                SerializedProperty animProp = elem.FindPropertyRelative("animatedParts");
                animProp.arraySize = b.animatedParts != null ? b.animatedParts.Length : 0;
                for (int a = 0; a < animProp.arraySize; a++)
                {
                    animProp.GetArrayElementAtIndex(a).objectReferenceValue = b.animatedParts[a];
                }
            }

            SerializedProperty presetsProp = so.FindProperty("cameraPresets");
            presetsProp.arraySize = cameraPresets.Count;
            for (int i = 0; i < cameraPresets.Count; i++)
            {
                SerializedProperty elem = presetsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("presetName").stringValue = cameraPresets[i].presetName;
                elem.FindPropertyRelative("anchorTransform").objectReferenceValue = cameraPresets[i].anchorTransform;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void RegisterSceneInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene existing in scenes)
            {
                if (string.Equals(existing.path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    existing.enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
