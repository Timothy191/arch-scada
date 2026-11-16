using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArchScada.Unity
{
    /// <summary>
    /// Maps read-only SCADA equipment telemetry and Unity3DEffectSettings received from
    /// ScadaUdpReceiver onto 3D scene GameObjects, renderers, particle systems, and camera presets.
    /// </summary>
    public class PlantDigitalTwinController : MonoBehaviour
    {
        [Serializable]
        public class EquipmentBinding
        {
            [Tooltip("Equipment ID matching ScadaModels (e.g., CR-101, TK-201, CV-101, PP-201).")]
            public string equipmentId;

            [Tooltip("Renderers whose color/emission reflect equipment status and thermal heatmap.")]
            public Renderer[] statusRenderers;

            [Tooltip("Optional halo GameObject activated when ShowAlarmHalos is enabled and equipment is Warning/Fault.")]
            public GameObject alarmHaloObject;

            [Tooltip("Optional particle systems controlled by ShowFlowParticles and Running/Warning state.")]
            public ParticleSystem[] flowParticles;

            [Tooltip("Optional transforms (conveyor rollers, thickener rake, crusher shaft) animated when AnimateConveyors is enabled.")]
            public Transform[] animatedParts;

            public Vector3 rotationAxis = Vector3.up;
            public float animationSpeedDegreesPerSec = 45f;

            [NonSerialized] public string currentStatus = "Stopped";
            [NonSerialized] public double currentLoadPercent;
            [NonSerialized] public double currentPowerKw;
        }

        [Serializable]
        public class CameraPresetTarget
        {
            public string presetName;
            public Transform anchorTransform;
        }

        [Header("Telemetry Source")]
        [SerializeField] private ScadaUdpReceiver udpReceiver;

        [Header("Equipment Scene Bindings")]
        [SerializeField] private List<EquipmentBinding> equipmentBindings = new List<EquipmentBinding>();

        [Header("Camera Presets")]
        [SerializeField] private Transform cameraRig;
        [SerializeField] private float cameraLerpSpeed = 5f;
        [SerializeField] private List<CameraPresetTarget> cameraPresets = new List<CameraPresetTarget>();

        [Header("Status Colors")]
        [SerializeField] private Color runningColor = new Color(0.133f, 0.773f, 0.369f);   // #22C55E
        [SerializeField] private Color warningColor = new Color(0.961f, 0.620f, 0.043f);   // #F59E0B
        [SerializeField] private Color faultColor = new Color(0.937f, 0.267f, 0.267f);     // #EF4444
        [SerializeField] private Color stoppedColor = new Color(0.392f, 0.455f, 0.545f);   // #64748B

        [Header("Thermal Heatmap Colors")]
        [SerializeField] private Color thermalCoolColor = new Color(0.145f, 0.388f, 0.922f); // #2563EB
        [SerializeField] private Color thermalWarmColor = new Color(0.976f, 0.451f, 0.086f); // #F97316
        [SerializeField] private Color thermalHotColor = new Color(0.863f, 0.149f, 0.149f);  // #DC2626

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly Dictionary<string, EquipmentBinding> _bindingLookup = new Dictionary<string, EquipmentBinding>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _cameraLookup = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        private MaterialPropertyBlock _propertyBlock;
        private Unity3DEffectSettingsDto _activeEffects = new Unity3DEffectSettingsDto();
        private Transform _targetCameraAnchor;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();

            if (udpReceiver == null)
            {
                udpReceiver = GetComponent<ScadaUdpReceiver>();
                if (udpReceiver == null)
                {
                    udpReceiver = FindFirstObjectByType<ScadaUdpReceiver>();
                }
            }

            RebuildLookups();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                RebuildLookups();
            }
        }

        private void Update()
        {
            if (udpReceiver != null && udpReceiver.ConsumeLatestFrame(out var frame) && frame != null)
            {
                ApplyFrame(frame);
            }

            UpdateAnimations(Time.deltaTime);
            UpdateCameraRig(Time.deltaTime);
        }

        public void RebuildLookups()
        {
            _bindingLookup.Clear();
            foreach (var binding in equipmentBindings)
            {
                if (binding != null && !string.IsNullOrWhiteSpace(binding.equipmentId))
                {
                    _bindingLookup[binding.equipmentId.Trim()] = binding;
                }
            }

            _cameraLookup.Clear();
            foreach (var preset in cameraPresets)
            {
                if (preset != null && !string.IsNullOrWhiteSpace(preset.presetName) && preset.anchorTransform != null)
                {
                    _cameraLookup[preset.presetName.Trim()] = preset.anchorTransform;
                }
            }
        }

        public void ApplyFrame(ScadaTelemetryFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            // Read-only digital twin: never issue outbound commands.
            _activeEffects = frame.effects ?? _activeEffects;

            if (!string.IsNullOrWhiteSpace(_activeEffects.CameraPreset) &&
                _cameraLookup.TryGetValue(_activeEffects.CameraPreset.Trim(), out var anchor))
            {
                _targetCameraAnchor = anchor;
            }

            if (frame.equipment == null)
            {
                return;
            }

            foreach (var item in frame.equipment)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id))
                {
                    continue;
                }

                if (!_bindingLookup.TryGetValue(item.Id.Trim(), out var binding))
                {
                    continue;
                }

                binding.currentStatus = string.IsNullOrWhiteSpace(item.status) ? "Stopped" : item.status;
                binding.currentLoadPercent = item.LoadPercent;
                binding.currentPowerKw = item.PowerKw;

                ApplyEquipmentVisuals(binding, _activeEffects);
            }
        }

        private void ApplyEquipmentVisuals(EquipmentBinding binding, Unity3DEffectSettingsDto fx)
        {
            bool isRunningOrWarning = IsActiveStatus(binding.currentStatus);
            bool isWarningOrFault = string.Equals(binding.currentStatus, "Warning", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(binding.currentStatus, "Fault", StringComparison.OrdinalIgnoreCase);

            Color targetColor = ResolveEquipmentColor(binding, fx.ShowThermalHeatmap);
            float emissionIntensity = string.Equals(binding.currentStatus, "Fault", StringComparison.OrdinalIgnoreCase) ? 1.5f : 0.45f;

            if (binding.statusRenderers != null)
            {
                foreach (var rend in binding.statusRenderers)
                {
                    if (rend == null)
                    {
                        continue;
                    }

                    rend.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(BaseColorId, targetColor);
                    _propertyBlock.SetColor(ColorId, targetColor);
                    _propertyBlock.SetColor(EmissionColorId, targetColor * emissionIntensity);
                    rend.SetPropertyBlock(_propertyBlock);
                }
            }

            if (binding.alarmHaloObject != null)
            {
                binding.alarmHaloObject.SetActive(fx.ShowAlarmHalos && isWarningOrFault);
            }

            if (binding.flowParticles != null)
            {
                bool shouldPlayFlow = fx.ShowFlowParticles && isRunningOrWarning;
                foreach (var ps in binding.flowParticles)
                {
                    if (ps == null)
                    {
                        continue;
                    }

                    if (shouldPlayFlow && !ps.isPlaying)
                    {
                        ps.Play();
                    }
                    else if (!shouldPlayFlow && ps.isPlaying)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
        }

        private Color ResolveEquipmentColor(EquipmentBinding binding, bool showThermalHeatmap)
        {
            if (showThermalHeatmap)
            {
                float t = Mathf.Clamp01((float)(binding.currentLoadPercent / 100.0));
                return t < 0.5f
                    ? Color.Lerp(thermalCoolColor, thermalWarmColor, t * 2f)
                    : Color.Lerp(thermalWarmColor, thermalHotColor, (t - 0.5f) * 2f);
            }

            if (string.Equals(binding.currentStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return runningColor;
            }

            if (string.Equals(binding.currentStatus, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                return warningColor;
            }

            if (string.Equals(binding.currentStatus, "Fault", StringComparison.OrdinalIgnoreCase))
            {
                return faultColor;
            }

            return stoppedColor;
        }

        private void UpdateAnimations(float deltaTime)
        {
            if (!_activeEffects.AnimateConveyors)
            {
                return;
            }

            foreach (var binding in equipmentBindings)
            {
                if (binding == null || binding.animatedParts == null || !IsActiveStatus(binding.currentStatus))
                {
                    continue;
                }

                float angle = binding.animationSpeedDegreesPerSec * deltaTime;
                foreach (var part in binding.animatedParts)
                {
                    if (part != null)
                    {
                        part.Rotate(binding.rotationAxis, angle, Space.Self);
                    }
                }
            }
        }

        private void UpdateCameraRig(float deltaTime)
        {
            if (cameraRig == null || _targetCameraAnchor == null)
            {
                return;
            }

            float t = 1f - Mathf.Exp(-cameraLerpSpeed * deltaTime);
            cameraRig.position = Vector3.Lerp(cameraRig.position, _targetCameraAnchor.position, t);
            cameraRig.rotation = Quaternion.Slerp(cameraRig.rotation, _targetCameraAnchor.rotation, t);
        }

        private static bool IsActiveStatus(string status)
        {
            return string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Warning", StringComparison.OrdinalIgnoreCase);
        }
    }
}
