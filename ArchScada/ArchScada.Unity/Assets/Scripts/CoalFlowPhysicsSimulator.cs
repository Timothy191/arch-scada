using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArchScada.Unity
{
    /// <summary>
    /// Simulates bulk-coal lumps under Unity PhysX gravity using an object pool.
    /// Coal spawns at <see cref="crusherHopperOrigin"/>, falls onto <see cref="screenCollider"/>
    /// and <see cref="conveyorColliders"/> (receiving tangential belt velocity driven by live
    /// CV-101/CV-102 telemetry from <see cref="ScadaUdpReceiver"/>), and despawns at
    /// <see cref="siloDropZone"/>. When a downstream conveyor is Stopped or Fault while an
    /// upstream unit is Running, belt velocity drops to zero so incoming lumps physically pile up.
    /// </summary>
    public class CoalFlowPhysicsSimulator : MonoBehaviour
    {
        [Serializable]
        public class ConveyorColliderBinding
        {
            [Tooltip("Equipment ID matching SCADA telemetry (e.g., CV-101, CV-102).")]
            public string equipmentId = "CV-101";

            [Tooltip("Collider representing the conveyor belt surface.")]
            public Collider beltCollider;

            [Tooltip("Local or world direction of coal travel along this conveyor.")]
            public Vector3 beltDirection = Vector3.right;

            [Tooltip("If true, beltDirection is transformed by beltCollider.transform.")]
            public bool useColliderLocalDirection = true;

            [Tooltip("Nominal belt speed (m/s) when Running at 100% load if PrimaryMetric is zero.")]
            public float nominalSpeedMetersPerSec = 2.2f;

            [Tooltip("Maximum clamped tangential belt speed (m/s).")]
            public float maxSpeedMetersPerSec = 5.0f;

            [NonSerialized] public string currentStatus = "Stopped";
            [NonSerialized] public float currentSpeedMetersPerSec;
        }

        private class PooledCoalLump
        {
            public GameObject gameObject;
            public Rigidbody rigidbody;
            public CoalLumpContactRelay contactRelay;
            public float spawnTime;
            public bool isActive;
        }

        [Header("Telemetry Source")]
        [SerializeField] private ScadaUdpReceiver udpReceiver;
        [SerializeField] private string crusherEquipmentId = "CR-101";
        [SerializeField] private string screenEquipmentId = "SC-102";

        [Header("Scene Anchors & Colliders")]
        [SerializeField] private Transform crusherHopperOrigin;
        [SerializeField] private Collider screenCollider;
        [SerializeField] private List<ConveyorColliderBinding> conveyorColliders = new List<ConveyorColliderBinding>();
        [SerializeField] private Collider siloDropZone;

        [Header("Coal Lump Pool Settings")]
        [SerializeField] private GameObject coalLumpPrefab;
        [SerializeField] private int poolSize = 120;
        [SerializeField] private float spawnIntervalSeconds = 0.14f;
        [SerializeField] private float spawnJitterRadius = 0.22f;
        [SerializeField] private float lumpScaleMin = 0.09f;
        [SerializeField] private float lumpScaleMax = 0.17f;
        [SerializeField] private float lumpMass = 1.5f;
        [SerializeField] private float maxLumpLifetimeSeconds = 35f;
        [SerializeField] private Color fallbackCoalColor = new Color(0.11f, 0.11f, 0.12f, 1f);

        [Header("PhysX Belt & Screen Dynamics")]
        [SerializeField] private float beltAcceleration = 18f;
        [SerializeField] private float screenAgitationImpulse = 0.35f;
        [SerializeField] private bool simulateWithoutTelemetry = false;

        private readonly List<PooledCoalLump> _pool = new List<PooledCoalLump>();
        private readonly Dictionary<Collider, ConveyorColliderBinding> _conveyorByCollider = new Dictionary<Collider, ConveyorColliderBinding>();
        private readonly Dictionary<string, ConveyorColliderBinding> _conveyorById = new Dictionary<string, ConveyorColliderBinding>(StringComparer.OrdinalIgnoreCase);

        private Transform _poolContainer;
        private Material _fallbackCoalMaterial;
        private float _spawnTimer;
        private bool _animateConveyors = true;
        private bool _reducedMotion = false;
        private string _crusherStatus = "Stopped";
        private string _screenStatus = "Stopped";
        private double _crusherLoadPercent = 75.0;

        public bool IsChokePileupActive { get; private set; }
        public string ChokedConveyorId { get; private set; } = string.Empty;
        public int ActiveLumpCount { get; private set; }

        private void Awake()
        {
            if (udpReceiver == null)
            {
                udpReceiver = GetComponent<ScadaUdpReceiver>();
                if (udpReceiver == null)
                {
                    udpReceiver = FindFirstObjectByType<ScadaUdpReceiver>();
                }
            }

            RebuildConveyorLookups();
            InitializePool();
        }

        private void OnValidate()
        {
            poolSize = Mathf.Max(1, poolSize);
            spawnIntervalSeconds = Mathf.Max(0.02f, spawnIntervalSeconds);
            lumpScaleMin = Mathf.Max(0.02f, lumpScaleMin);
            lumpScaleMax = Mathf.Max(lumpScaleMin, lumpScaleMax);
            lumpMass = Mathf.Max(0.05f, lumpMass);

            if (Application.isPlaying)
            {
                RebuildConveyorLookups();
            }
        }

        private void OnDestroy()
        {
            if (_fallbackCoalMaterial != null)
            {
                Destroy(_fallbackCoalMaterial);
                _fallbackCoalMaterial = null;
            }
        }

        private void Update()
        {
            RefreshTelemetryState();
            UpdateChokePileupState();
        }

        private void FixedUpdate()
        {
            UpdateSpawning(Time.fixedDeltaTime);
            UpdateActiveLumps();
        }

        public void RebuildConveyorLookups()
        {
            _conveyorByCollider.Clear();
            _conveyorById.Clear();

            foreach (var binding in conveyorColliders)
            {
                if (binding == null)
                {
                    continue;
                }

                if (binding.beltCollider != null)
                {
                    _conveyorByCollider[binding.beltCollider] = binding;
                }

                if (!string.IsNullOrWhiteSpace(binding.equipmentId))
                {
                    _conveyorById[binding.equipmentId.Trim()] = binding;
                }
            }
        }

        private void InitializePool()
        {
            var containerGo = new GameObject("CoalLumpPool");
            containerGo.transform.SetParent(transform, false);
            _poolContainer = containerGo.transform;

            if (coalLumpPrefab == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    _fallbackCoalMaterial = new Material(shader)
                    {
                        color = fallbackCoalColor
                    };
                }
            }

            for (int i = 0; i < poolSize; i++)
            {
                _pool.Add(CreatePooledLump(i));
            }
        }

        private PooledCoalLump CreatePooledLump(int index)
        {
            GameObject go;
            if (coalLumpPrefab != null)
            {
                go = Instantiate(coalLumpPrefab, _poolContainer);
                go.name = $"CoalLump_{index:D3}";
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"CoalLump_{index:D3}";
                go.transform.SetParent(_poolContainer, false);
                var rend = go.GetComponent<Renderer>();
                if (rend != null && _fallbackCoalMaterial != null)
                {
                    rend.sharedMaterial = _fallbackCoalMaterial;
                }
            }

            float scale = UnityEngine.Random.Range(lumpScaleMin, lumpScaleMax);
            go.transform.localScale = new Vector3(
                scale * UnityEngine.Random.Range(0.85f, 1.25f),
                scale * UnityEngine.Random.Range(0.75f, 1.10f),
                scale * UnityEngine.Random.Range(0.85f, 1.25f));

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = go.AddComponent<Rigidbody>();
            }

            rb.mass = lumpMass;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (go.GetComponent<Collider>() == null)
            {
                go.AddComponent<SphereCollider>();
            }

            var relay = go.GetComponent<CoalLumpContactRelay>();
            if (relay == null)
            {
                relay = go.AddComponent<CoalLumpContactRelay>();
            }

            var lump = new PooledCoalLump
            {
                gameObject = go,
                rigidbody = rb,
                contactRelay = relay,
                isActive = false
            };

            relay.Initialize(this, lump);
            go.SetActive(false);
            return lump;
        }

        private void RefreshTelemetryState()
        {
            if (udpReceiver == null || !udpReceiver.TryGetLatestFrame(out var frame) || frame == null)
            {
                if (simulateWithoutTelemetry)
                {
                    _animateConveyors = true;
                    _crusherStatus = "Running";
                    _screenStatus = "Running";
                    foreach (var binding in conveyorColliders)
                    {
                        if (binding != null)
                        {
                            binding.currentStatus = "Running";
                            binding.currentSpeedMetersPerSec = binding.nominalSpeedMetersPerSec;
                        }
                    }
                }
                return;
            }

            _animateConveyors = frame.effects == null || frame.effects.AnimateConveyors;

            // Respect reduced motion preference
            if (_reducedMotion)
            {
                _animateConveyors = false;
                _crusherStatus = "Stopped";
                _screenStatus = "Stopped";
                foreach (var binding in conveyorColliders)
                {
                    if (binding != null)
                    {
                        binding.currentStatus = "Stopped";
                        binding.currentSpeedMetersPerSec = 0f;
                    }
                }
                return;
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

                if (string.Equals(id, crusherEquipmentId, StringComparison.OrdinalIgnoreCase))
                {
                    _crusherStatus = status;
                    _crusherLoadPercent = item.LoadPercent;
                }
                else if (string.Equals(id, screenEquipmentId, StringComparison.OrdinalIgnoreCase))
                {
                    _screenStatus = status;
                }
                else if (_conveyorById.TryGetValue(id, out var conveyor))
                {
                    conveyor.currentStatus = status;
                    conveyor.currentSpeedMetersPerSec = ResolveBeltSpeed(conveyor, item);
                }
            }
        }

        private float ResolveBeltSpeed(ConveyorColliderBinding binding, EquipmentTelemetryDto item)
        {
            if (!_animateConveyors || !IsRunningOrWarning(binding.currentStatus))
            {
                return 0f;
            }

            float speed = item.PrimaryMetric > 0.01
                ? (float)item.PrimaryMetric
                : binding.nominalSpeedMetersPerSec * Mathf.Clamp01((float)(item.LoadPercent / 100.0));

            if (speed <= 0.001f)
            {
                speed = binding.nominalSpeedMetersPerSec;
            }

            return Mathf.Clamp(speed, 0f, binding.maxSpeedMetersPerSec);
        }

        private void UpdateChokePileupState()
        {
            IsChokePileupActive = false;
            ChokedConveyorId = string.Empty;

            if (conveyorColliders.Count == 0)
            {
                return;
            }

            bool upstreamRunning = IsRunningOrWarning(_crusherStatus);
            for (int i = 0; i < conveyorColliders.Count; i++)
            {
                var belt = conveyorColliders[i];
                if (belt == null)
                {
                    continue;
                }

                bool beltRunning = IsRunningOrWarning(belt.currentStatus);
                if (!beltRunning && upstreamRunning)
                {
                    IsChokePileupActive = true;
                    ChokedConveyorId = belt.equipmentId;
                    return;
                }

                if (beltRunning)
                {
                    upstreamRunning = true;
                }
            }
        }

        private void UpdateSpawning(float dt)
        {
            if (crusherHopperOrigin == null || !IsRunningOrWarning(_crusherStatus))
            {
                return;
            }

            float loadFactor = Mathf.Clamp((float)(_crusherLoadPercent / 100.0), 0.35f, 1.5f);
            float effectiveInterval = spawnIntervalSeconds / loadFactor;

            _spawnTimer += dt;
            if (_spawnTimer < effectiveInterval)
            {
                return;
            }

            _spawnTimer = 0f;
            SpawnCoalLump();
        }

        private void SpawnCoalLump()
        {
            PooledCoalLump lump = AcquireLump();
            if (lump == null)
            {
                return;
            }

            Vector2 jitter = UnityEngine.Random.insideUnitCircle * spawnJitterRadius;
            Vector3 spawnPos = crusherHopperOrigin.position + new Vector3(jitter.x, 0f, jitter.y);

            lump.isActive = true;
            lump.spawnTime = Time.time;
            lump.gameObject.transform.position = spawnPos;
            lump.gameObject.transform.rotation = UnityEngine.Random.rotation;
            lump.gameObject.SetActive(true);

            lump.rigidbody.velocity = new Vector3(
                UnityEngine.Random.Range(-0.15f, 0.15f),
                -0.25f,
                UnityEngine.Random.Range(-0.15f, 0.15f));
            lump.rigidbody.angularVelocity = UnityEngine.Random.insideUnitSphere * 1.5f;
        }

        private PooledCoalLump AcquireLump()
        {
            PooledCoalLump oldestActive = null;
            float oldestTime = float.MaxValue;

            for (int i = 0; i < _pool.Count; i++)
            {
                var candidate = _pool[i];
                if (!candidate.isActive)
                {
                    return candidate;
                }

                if (candidate.spawnTime < oldestTime)
                {
                    oldestTime = candidate.spawnTime;
                    oldestActive = candidate;
                }
            }

            return oldestActive;
        }

        private void UpdateActiveLumps()
        {
            int activeCount = 0;
            float now = Time.time;

            for (int i = 0; i < _pool.Count; i++)
            {
                var lump = _pool[i];
                if (!lump.isActive)
                {
                    continue;
                }

                if (now - lump.spawnTime > maxLumpLifetimeSeconds ||
                    lump.gameObject.transform.position.y < -15f ||
                    (siloDropZone != null && siloDropZone.bounds.Contains(lump.gameObject.transform.position)))
                {
                    DespawnLump(lump);
                    continue;
                }

                activeCount++;
            }

            ActiveLumpCount = activeCount;
        }

        private void DespawnLump(PooledCoalLump lump)
        {
            if (lump == null || !lump.isActive)
            {
                return;
            }

            lump.isActive = false;
            lump.rigidbody.velocity = Vector3.zero;
            lump.rigidbody.angularVelocity = Vector3.zero;
            lump.gameObject.SetActive(false);
        }

        internal void HandleLumpCollisionStay(PooledCoalLump lump, Collider hitCollider)
        {
            if (lump == null || !lump.isActive || hitCollider == null)
            {
                return;
            }

            if (siloDropZone != null && hitCollider == siloDropZone)
            {
                DespawnLump(lump);
                return;
            }

            if (screenCollider != null && hitCollider == screenCollider)
            {
                if (IsRunningOrWarning(_screenStatus))
                {
                    Vector3 jitter = new Vector3(
                        UnityEngine.Random.Range(-0.2f, 0.4f),
                        UnityEngine.Random.Range(0.15f, 0.5f),
                        UnityEngine.Random.Range(-0.2f, 0.2f));
                    lump.rigidbody.AddForce(jitter * screenAgitationImpulse, ForceMode.Impulse);
                }
                return;
            }

            if (_conveyorByCollider.TryGetValue(hitCollider, out var belt))
            {
                ApplyConveyorVelocity(lump.rigidbody, belt);
            }
        }

        internal void HandleLumpTriggerEnter(PooledCoalLump lump, Collider triggerCollider)
        {
            if (lump != null && lump.isActive && siloDropZone != null && triggerCollider == siloDropZone)
            {
                DespawnLump(lump);
            }
        }

        private void ApplyConveyorVelocity(Rigidbody rb, ConveyorColliderBinding belt)
        {
            Vector3 dir = belt.useColliderLocalDirection && belt.beltCollider != null
                ? belt.beltCollider.transform.TransformDirection(belt.beltDirection)
                : belt.beltDirection;

            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            dir.Normalize();
            float targetSpeed = belt.currentSpeedMetersPerSec;

            Vector3 currentVel = rb.velocity;
            Vector3 desiredHorizontal = dir * targetSpeed;
            Vector3 delta = new Vector3(desiredHorizontal.x - currentVel.x, 0f, desiredHorizontal.z - currentVel.z);

            rb.AddForce(delta * beltAcceleration, ForceMode.Acceleration);
        }

        private static bool IsRunningOrWarning(string status)
        {
            return string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Warning", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the reduced motion preference. When enabled, all animated effects
        /// (conveyor belt movement, coal pileup physics) are disabled and replaced
        /// with static states, respecting accessibility preferences.
        /// </summary>
        public void SetReducedMotion(bool enabled)
        {
            _reducedMotion = enabled;
            // If just disabled, allow physics to resume on next update
        }

        private sealed class CoalLumpContactRelay : MonoBehaviour
        {
            private CoalFlowPhysicsSimulator _simulator;
            private PooledCoalLump _lump;

            public void Initialize(CoalFlowPhysicsSimulator simulator, PooledCoalLump lump)
            {
                _simulator = simulator;
                _lump = lump;
            }

            private void OnCollisionStay(Collision collision)
            {
                if (_simulator != null && collision != null)
                {
                    _simulator.HandleLumpCollisionStay(_lump, collision.collider);
                }
            }

            private void OnTriggerEnter(Collider other)
            {
                if (_simulator != null)
                {
                    _simulator.HandleLumpTriggerEnter(_lump, other);
                }
            }
        }
    }
}
