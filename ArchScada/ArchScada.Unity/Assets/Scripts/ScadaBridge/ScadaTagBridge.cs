using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArchScada.DigitalTwin.ScadaBridge
{
    /// <summary>
    /// Bridge between live SCADA tag values and the Unity 3D digital twin.
    /// Receives tag updates from the SCADA server (via WebSocket, MQTT, or REST)
    /// and makes them available to digital twin components.
    /// </summary>
    public class ScadaTagBridge : MonoBehaviour
    {
        private static ScadaTagBridge _instance;
        public static ScadaTagBridge Instance => _instance;

        /// <summary>
        /// Latest tag values keyed by tag name (e.g. "TankLevel", "TankPump").
        /// </summary>
        private readonly Dictionary<string, double> _tagValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Latest tag statuses keyed by tag name.
        /// </summary>
        private readonly Dictionary<string, int> _tagStatuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Timestamp of the last update (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; private set; }

        public event Action<string, double, int> OnTagUpdated;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Called by the SCADA communication layer when a tag value changes.
        /// </summary>
        public void UpdateTag(string tagName, double value, int status)
        {
            _tagValues[tagName] = value;
            _tagStatuses[tagName] = status;
            LastUpdateUtc = DateTime.UtcNow;
            OnTagUpdated?.Invoke(tagName, value, status);
        }

        /// <summary>
        /// Gets the current value of a tag. Returns double.NaN if the tag is unknown.
        /// </summary>
        public double GetTagValue(string tagName)
        {
            return _tagValues.TryGetValue(tagName, out var v) ? v : double.NaN;
        }

        /// <summary>
        /// Gets the current status of a tag. Returns CnlStatusID.Undefined if unknown.
        /// </summary>
        public int GetTagStatus(string tagName)
        {
            return _tagStatuses.TryGetValue(tagName, out var s) ? s : 0;
        }

        /// <summary>
        /// Returns true if the tag exists and has a defined status.
        /// </summary>
        public bool IsTagDefined(string tagName)
        {
            return _tagStatuses.TryGetValue(tagName, out var s) && s > 0; // CnlStatusID.Undefined = 0
        }
    }
}