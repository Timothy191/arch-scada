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
    /// lays out the Coal Handling & Preparation Plant (CHPP) 3D scene, wires
    /// ScadaUdpReceiver + PlantDigitalTwinController, and saves CoalPlantTwin.unity.
    /// </summary>
    public static class ArchScadaSceneBuilder
    {
        private const string ModelsAssetFolder = "Assets/Models";
        private const string ScenesAssetFolder = "Assets/Scenes";
        private const string SceneAssetPath = "Assets/Scenes/CoalPlantTwin.unity";

        private static readonly string[] GlbFileNames =
        {
            "crusher_cr101.glb",
            "thickener_tk201.glb",
            "centrifugal_pump.glb",
            "conveyor_segment.glb",
            "flotation_column_fc401.glb",
            "centrifuge_cf501.glb",
            "coal_silo_sl601.glb"
        };

        private struct EquipmentSpec
        {
            public string Id;
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
            // Crushing & Conveyors (Left Zone)
            new EquipmentSpec("CR-101", "crusher_cr101.glb", new Vector3(-7.5f, 0f, -2.5f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(2.2f, 1.8f, 2.0f), true, Vector3.up, 30f),
            new EquipmentSpec("CV-101", "conveyor_segment.glb", new Vector3(-4.2f, 0.35f, -2.5f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(4.2f, 0.5f, 1.0f)),
            new EquipmentSpec("SC-102", null, new Vector3(-1.3f, 0.75f, -2.5f), Vector3.zero, Vector3.one, PrimitiveType.Cube, new Vector3(1.8f, 1.5f, 1.8f), true, Vector3.right, 60f),
            new EquipmentSpec("CV-102", "conveyor_segment.glb", new Vector3(-1.3f, 0.3f, 0.6f), new Vector3(0f, 90f, 0f), new Vector3(0.75f, 1f, 1f), PrimitiveType.Cube, new Vector3(1.1f, 0.4f, 3.8f)),
            new EquipmentSpec("SL-601", "coal_silo_sl601.glb", new Vector3(-7.5f, 0f, 3.2f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(3.0f, 4.0f, 3.0f)),

            // Thickener Circuit (Center-Front Zone)
            new EquipmentSpec("TK-201", "thickener_tk201.glb", new Vector3(1.2f, 0f, 3.2f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(5.6f, 0.9f, 5.6f), true, Vector3.up, 18f),
            new EquipmentSpec("PP-201", "centrifugal_pump.glb", new Vector3(-2.4f, 0f, 4.2f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(1.0f, 0.9f, 1.0f), true, Vector3.right, 120f),
            new EquipmentSpec("PP-202", "centrifugal_pump.glb", new Vector3(-2.4f, 0f, 5.5f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(1.0f, 0.9f, 1.0f), true, Vector3.right, 120f),
            new EquipmentSpec("PP-203", "centrifugal_pump.glb", new Vector3(4.6f, 0f, 4.2f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(0.9f, 0.8f, 0.9f), true, Vector3.right, 120f),

            // Magnetite Preparation & Fine Coal Processing (Right Zone)
            new EquipmentSpec("TK-301", null, new Vector3(5.8f, 1.2f, -2.2f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(2.8f, 1.2f, 2.8f)),
            new EquipmentSpec("MS-301", null, new Vector3(3.2f, 0.7f, -2.2f), new Vector3(0f, 0f, 90f), Vector3.one, PrimitiveType.Cylinder, new Vector3(1.6f, 0.8f, 1.6f), true, Vector3.up, 75f),
            new EquipmentSpec("DM-301", "centrifugal_pump.glb", new Vector3(5.8f, 0f, 0.8f), new Vector3(0f, -90f, 0f), new Vector3(1.2f, 1.2f, 1.2f), PrimitiveType.Cube, new Vector3(1.4f, 1.1f, 1.2f), true, Vector3.right, 140f),
            new EquipmentSpec("FC-401", "flotation_column_fc401.glb", new Vector3(8.8f, 0f, -2.0f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(2.0f, 3.0f, 2.0f)),
            new EquipmentSpec("CF-501", "centrifuge_cf501.glb", new Vector3(8.8f, 0f, 1.8f), Vector3.zero, Vector3.one, PrimitiveType.Cylinder, new Vector3(1.8f, 1.2f, 1.8f), true, Vector3.right, 180f)
        };

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

            Debug.Log($"[ArchScadaSceneBuilder] Built and saved 3D Digital Twin scene at {SceneAssetPath} with {bindings.Count} equipment bindings.");
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
            pad.transform.position = Vector3.zero;
            pad.transform.localScale = new Vector3(3.2f, 1f, 2.4f);

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
                    equipmentId = spec.Id,
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
            rigObj.transform.position = new Vector3(0f, 14f, -16f);
            rigObj.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

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
                CreatePreset(presetsRoot.transform, "Plant Overview", new Vector3(0.5f, 14.5f, -16.5f), new Vector3(36f, 0f, 0f)),
                CreatePreset(presetsRoot.transform, "Crushing & Conveyors", new Vector3(-5.2f, 7.5f, -10.5f), new Vector3(30f, 12f, 0f)),
                CreatePreset(presetsRoot.transform, "Thickener Circuit", new Vector3(0.8f, 8.5f, -5.0f), new Vector3(34f, 5f, 0f)),
                CreatePreset(presetsRoot.transform, "Magnetite Prep", new Vector3(6.2f, 8.0f, -9.5f), new Vector3(32f, -12f, 0f)),
                CreatePreset(presetsRoot.transform, "Top-Down Schematic", new Vector3(0.5f, 24.0f, 0.5f), new Vector3(88f, 0f, 0f))
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
