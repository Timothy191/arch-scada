using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArchScada.Unity
{
    /// <summary>
    /// URP visual enhancer driven by read-only SCADA telemetry from <see cref="ScadaUdpReceiver"/>.
    /// Scrolls conveyor belt UV offsets (_BaseMap / _MainTex) proportional to live belt speed,
    /// applies telemetry-driven vibration oscillation to SC-102 and CF-501 transforms, and
    /// toggles dust ParticleSystem emission at CR-101.
    /// </summary>
    public class UrpVisualEnhancer : MonoBehaviour
    {
        [Serializable]
        public class ConveyorBeltUvBinding
        {
            [Tooltip("Equipment ID matching SCADA telemetry (e.g., CV-101, CV-102).")]
            public string equipmentId = "CV-101";

            [Tooltip("Renderer whose belt material _BaseMap/_MainTex UV offset will scroll.")]
            public Renderer beltRenderer;

            [Tooltip("Material slot index on beltRenderer.")]
            public int materialIndex;

            [Tooltip("Normalized UV scroll direction.")]
            public Vector2 uvScrollDirection = new Vector2(0f, 1f);

            [Tooltip("UV scroll multiplier per m/s of belt speed.")]
            public float uvSpeedMultiplier = 0.5f;

            [Tooltip("Nominal speed (m/s) when Running if PrimaryMetric is zero.")]
            public float nominalSpeedMetersPerSec = 2.0f;

            [NonSerialized] public Vector2 baseTiling = Vector2.one;
            [NonSerialized] public Vector2 currentOffset;
            [NonSerialized] public float currentSpeedMetersPerSec;
            [NonSerialized] public string currentStatus = "Stopped";
        }

        [Header("Telemetry Source")]
        [SerializeField] private ScadaUdpReceiver udpReceiver;

        [Header("Conveyor Belt UV Scrolling")]
        [SerializeField] private List<ConveyorBeltUvBinding> conveyorBelts = new List<ConveyorBeltUvBinding>();

        [Header("Telemetry-Driven Vibration (SC-102 & CF-501)")]
        [SerializeField] private Transform sc102Transform;
        [SerializeField] private float sc102BaseAmplitude = 0.018f;
        [SerializeField] private float sc102FrequencyHz = 24f;
        [SerializeField] private Vector3 sc102AxisMask = new Vector3(0.4f, 1.0f, 0.4f);

        [SerializeField] private Transform cf501Transform;
        [SerializeField] private float cf501BaseAmplitude = 0.012f;
        [SerializeField] private float cf501FrequencyHz = 36f;
        [SerializeField] private Vector3 cf501AxisMask = new Vector3(1.0f, 0.25f, 1.0f);

        [Header("CR-101 Dust Particle Emission")]
        [SerializeField] private ParticleSystem cr101DustParticles;
        [SerializeField] private string crusherEquipmentId = "CR-101";
        [SerializeField] private float dustMaxRateMultiplier = 1.5f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

        private readonly Dictionary<string, ConveyorBeltUvBinding> _beltLookup = new Dictionary<string, ConveyorBeltUvBinding>(StringComparer.OrdinalIgnoreCase);
        private MaterialPropertyBlock _propertyBlock;

        private Vector3 _sc102RestLocalPos;
        private bool _hasSc102RestPos;
        private Vector3 _cf501RestLocalPos;
        private bool _hasCf501RestPos;

        private float _sc102Intensity;
        private float _cf501Intensity;
        private string _sc102Status = "Stopped";
        private double _sc102LoadPercent;
        private string _cf501Status = "Stopped";
        private double _cf501LoadPercent;

        private string _cr101Status = "Stopped";
        private double _cr101LoadPercent;
        private bool _animateConveyors = true;
        private bool _showFlowParticles = true;
        private bool _reducedMotion = false;

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

            CacheRestTransforms();
            RebuildBeltLookup();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                RebuildBeltLookup();
            }
        }

        private void Update()
        {
            RefreshTelemetry();
            UpdateConveyorUvScrolling(Time.deltaTime);
            UpdateVibrationTransforms(Time.deltaTime);
            UpdateCrusherDustEmission();
        }

        public void RebuildBeltLookup()
        {
            _beltLookup.Clear();
            foreach (var belt in conveyorBelts)
            {
                if (belt == null || string.IsNullOrWhiteSpace(belt.equipmentId))
                {
                    continue;
                }

                CacheMaterialTiling(belt);
                _beltLookup[belt.equipmentId.Trim()] = belt;
            }
        }

        private void CacheRestTransforms()
        {
            if (sc102Transform != null)
            {
                _sc102RestLocalPos = sc102Transform.localPosition;
                _hasSc102RestPos = true;
            }

            if (cf501Transform != null)
            {
                _cf501RestLocalPos = cf501Transform.localPosition;
                _hasCf501RestPos = true;
            }
        }

        private static void CacheMaterialTiling(ConveyorBeltUvBinding belt)
        {
            if (belt.beltRenderer == null)
            {
                return;
            }

            var sharedMats = belt.beltRenderer.sharedMaterials;
            if (sharedMats == null || belt.materialIndex < 0 || belt.materialIndex >= sharedMats.Length)
            {
                return;
            }

            var mat = sharedMats[belt.materialIndex];
            if (mat == null)
            {
                return;
            }

            if (mat.HasProperty(BaseMapId))
            {
                belt.baseTiling = mat.GetTextureScale(BaseMapId);
            }
            else if (mat.HasProperty(MainTexId))
            {
                belt.baseTiling = mat.GetTextureScale(MainTexId);
            }
        }

        private void RefreshTelemetry()
        {
            if (udpReceiver == null || !udpReceiver.TryGetLatestFrame(out var frame) || frame == null)
            {
                return;
            }

            if (frame.effects != null)
            {
                _animateConveyors = frame.effects.AnimateConveyors;
                _showFlowParticles = frame.effects.ShowFlowParticles;
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

                string id = item.Id.Trim();
                string status = string.IsNullOrWhiteSpace(item.status) ? "Stopped" : item.status.Trim();

                if (_beltLookup.TryGetValue(id, out var belt))
                {
                    belt.currentStatus = status;
                    belt.currentSpeedMetersPerSec = ResolveBeltSpeed(belt, item);
                }

                if (string.Equals(id, "SC-102", StringComparison.OrdinalIgnoreCase))
                {
                    _sc102Status = status;
                    _sc102LoadPercent = item.LoadPercent;
                }
                else if (string.Equals(id, "CF-501", StringComparison.OrdinalIgnoreCase))
                {
                    _cf501Status = status;
                    _cf501LoadPercent = item.LoadPercent;
                }
                else if (string.Equals(id, crusherEquipmentId, StringComparison.OrdinalIgnoreCase))
                {
                    _cr101Status = status;
                    _cr101LoadPercent = item.LoadPercent;
                }
            }
        }

        private float ResolveBeltSpeed(ConveyorBeltUvBinding belt, EquipmentTelemetryDto item)
        {
            if (!_animateConveyors || !IsRunningOrWarning(belt.currentStatus))
            {
                return 0f;
            }

            if (item.PrimaryMetric > 0.01)
            {
                return (float)item.PrimaryMetric;
            }

            float loadFactor = Mathf.Clamp((float)(item.LoadPercent / 100.0), 0.25f, 1.5f);
            return belt.nominalSpeedMetersPerSec * loadFactor;
        }

        private void UpdateConveyorUvScrolling(float dt)
        {
            // Respect reduced motion preference
            if (_reducedMotion)
            {
                // In reduced motion mode, stop UV animation but keep last known offset
                for (int i = 0; i < conveyorBelts.Count; i++)
                {
                    var belt = conveyorBelts[i];
                    if (belt == null || belt.beltRenderer == null) continue;
                    // Just ensure property block is set without animation
                    belt.beltRenderer.GetPropertyBlock(_propertyBlock, belt.materialIndex);
                    belt.beltRenderer.SetPropertyBlock(_propertyBlock, belt.materialIndex);
                }
                return;
            }

            for (int i = 0; i < conveyorBelts.Count; i++)
            {
                var belt = conveyorBelts[i];
                if (belt == null || belt.beltRenderer == null || belt.currentSpeedMetersPerSec <= 0.001f)
                {
                    continue;
                }

                Vector2 dir = belt.uvScrollDirection.sqrMagnitude > 0.0001f
                    ? belt.uvScrollDirection.normalized
                    : Vector2.up;

                belt.currentOffset += dir * (belt.currentSpeedMetersPerSec * belt.uvSpeedMultiplier * dt);
                belt.currentOffset.x %= 1f;
                belt.currentOffset.y %= 1f;

                Vector4 st = new Vector4(belt.baseTiling.x, belt.baseTiling.y, belt.currentOffset.x, belt.currentOffset.y);

                belt.beltRenderer.GetPropertyBlock(_propertyBlock, belt.materialIndex);
                _propertyBlock.SetVector(BaseMapStId, st);
                _propertyBlock.SetVector(MainTexStId, st);
                belt.beltRenderer.SetPropertyBlock(_propertyBlock, belt.materialIndex);
            }
        }

        private void UpdateVibrationTransforms(float dt)
        {
            // Respect reduced motion preference
            if (_reducedMotion)
            {
                // In reduced motion, reset transforms to rest position
                if (_hasSc102RestPos && sc102Transform != null)
                    sc102Transform.localPosition = _sc102RestLocalPos;
                if (_hasCf501RestPos && cf501Transform != null)
                    cf501Transform.localPosition = _cf501RestLocalPos;
                _sc102Intensity = 0f;
                _cf501Intensity = 0f;
                return;
            }

            _sc102Intensity = UpdateSingleVibration(
                sc102Transform,
                _hasSc102RestPos,
                _sc102RestLocalPos,
                _sc102Status,
                _sc102LoadPercent,
                _sc102Intensity,
                sc102BaseAmplitude,
                sc102FrequencyHz,
                sc102AxisMask,
                dt);

            _cf501Intensity = UpdateSingleVibration(
                cf501Transform,
                _hasCf501RestPos,
                _cf501RestLocalPos,
                _cf501Status,
                _cf501LoadPercent,
                _cf501Intensity,
                cf501BaseAmplitude,
                cf501FrequencyHz,
                cf501AxisMask,
                dt);
        }

        private static float UpdateSingleVibration(
            Transform target,
            bool hasRestPos,
            Vector3 restLocalPos,
            string status,
            double loadPercent,
            float currentIntensity,
            float baseAmplitude,
            float frequencyHz,
            Vector3 axisMask,
            float dt)
        {
            if (target == null || !hasRestPos)
            {
                return 0f;
            }

            float targetIntensity = IsRunningOrWarning(status)
                ? Mathf.Clamp((float)(loadPercent / 100.0), 0.35f, 1.4f)
                : 0f;

            currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, dt * 4f);

            if (currentIntensity <= 0.001f)
            {
                target.localPosition = restLocalPos;
                return 0f;
            }

            float omega = Time.time * frequencyHz * Mathf.PI * 2f;
            Vector3 offset = new Vector3(
                Mathf.Sin(omega) * axisMask.x,
                Mathf.Cos(omega * 1.13f) * axisMask.y,
                Mathf.Sin(omega * 0.87f + 0.7f) * axisMask.z);

            target.localPosition = restLocalPos + offset * (baseAmplitude * currentIntensity);
            return currentIntensity;
        }

        private void UpdateCrusherDustEmission()
        {
            // Respect reduced motion preference
            if (_reducedMotion)
            {
                if (cr101DustParticles == null) return;
                cr101DustParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            if (cr101DustParticles == null)
            {
                return;
            }

            bool shouldEmit = _showFlowParticles && IsRunningOrWarning(_cr101Status);
            var emission = cr101DustParticles.emission;
            emission.enabled = shouldEmit;

            if (shouldEmit)
            {
                float loadRatio = Mathf.Clamp((float)(_cr101LoadPercent / 100.0), 0.25f, 1.0f);
                emission.rateOverTimeMultiplier = Mathf.Lerp(0.4f, dustMaxRateMultiplier, loadRatio);

                if (!cr101DustParticles.isPlaying)
                {
                    cr101DustParticles.Play();
                }
            }
            else if (cr101DustParticles.isPlaying)
            {
                cr101DustParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private static bool IsRunningOrWarning(string status)
        {
            return string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Warning", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the reduced motion preference. When enabled, all animated effects
        /// (conveyor belt UV scrolling, vibration, particle systems) are disabled
        /// and replaced with static states, respecting accessibility preferences.
        /// </summary>
        public void SetReducedMotion(bool enabled)
        {
            _reducedMotion = enabled;
            // If just disabled, allow animations to resume on next update
        }
    }
}
