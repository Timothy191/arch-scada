using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ArchScada.Unity
{
    [Serializable]
    public class ScadaTelemetryFrame
    {
        public string timestampUtc;
        public bool readOnly = true;
        public Unity3DEffectSettingsDto effects;
        public EquipmentTelemetryDto[] equipment;
    }

    [Serializable]
    public class Unity3DEffectSettingsDto
    {
        public string CameraPreset = "Plant Overview";
        public bool ShowFlowParticles = true;
        public bool ShowThermalHeatmap;
        public bool ShowAlarmHalos = true;
        public bool AnimateConveyors = true;
    }

    [Serializable]
    public class EquipmentTelemetryDto
    {
        public string Id;
        public string Area;
        public string Type;
        public string status;
        public double PowerKw;
        public double LoadPercent;
        public double PrimaryMetric;
        public string PrimaryMetricUnit;
    }

    /// <summary>
    /// Listens on UDP port 17850 for JSON telemetry frames published by ArchScada.Desktop's
    /// UnityBridgeService and exposes thread-safe latest frame state to the Unity scene.
    /// </summary>
    public class ScadaUdpReceiver : MonoBehaviour
    {
        [Header("UDP Listener Settings")]
        [SerializeField] private int listenPort = 17850;
        [SerializeField] private bool autoStartOnEnable = true;
        [SerializeField] private float staleTimeoutSeconds = 5.0f;

        private readonly object _frameLock = new object();
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private volatile bool _isRunning;

        private ScadaTelemetryFrame _latestFrame;
        private bool _hasUnreadFrame;
        private DateTime _lastReceivedUtc = DateTime.MinValue;
        private long _packetsReceived;
        private long _parseErrors;

        public event Action<ScadaTelemetryFrame> OnFrameReceived;

        public int ListenPort => listenPort;

        public bool IsListening => _isRunning;

        public bool IsReceiving
        {
            get
            {
                lock (_frameLock)
                {
                    if (_lastReceivedUtc == DateTime.MinValue)
                    {
                        return false;
                    }

                    return (DateTime.UtcNow - _lastReceivedUtc).TotalSeconds <= staleTimeoutSeconds;
                }
            }
        }

        public long PacketsReceived
        {
            get
            {
                lock (_frameLock)
                {
                    return _packetsReceived;
                }
            }
        }

        public long ParseErrors
        {
            get
            {
                lock (_frameLock)
                {
                    return _parseErrors;
                }
            }
        }

        public DateTime LastReceivedUtc
        {
            get
            {
                lock (_frameLock)
                {
                    return _lastReceivedUtc;
                }
            }
        }

        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                StartListening();
            }
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void Update()
        {
            if (OnFrameReceived == null)
            {
                return;
            }

            if (ConsumeLatestFrame(out var frame) && frame != null)
            {
                OnFrameReceived.Invoke(frame);
            }
        }

        public void StartListening()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _udpClient = new UdpClient(listenPort);
                _udpClient.Client.ReceiveTimeout = 500;
                _isRunning = true;

                _receiveThread = new Thread(ReceiveLoop)
                {
                    Name = $"ScadaUdpReceiver:{listenPort}",
                    IsBackground = true
                };
                _receiveThread.Start();
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Debug.LogError($"[ScadaUdpReceiver] Failed to bind UDP port {listenPort}: {ex.Message}", this);
            }
        }

        public void StopListening()
        {
            _isRunning = false;

            if (_udpClient != null)
            {
                try
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
                catch
                {
                    // Ignore socket shutdown exceptions during teardown.
                }
                finally
                {
                    _udpClient = null;
                }
            }

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(250);
                _receiveThread = null;
            }
        }

        public bool TryGetLatestFrame(out ScadaTelemetryFrame frame)
        {
            lock (_frameLock)
            {
                frame = _latestFrame;
                return frame != null;
            }
        }

        public bool ConsumeLatestFrame(out ScadaTelemetryFrame frame)
        {
            lock (_frameLock)
            {
                if (!_hasUnreadFrame || _latestFrame == null)
                {
                    frame = null;
                    return false;
                }

                frame = _latestFrame;
                _hasUnreadFrame = false;
                return true;
            }
        }

        private void ReceiveLoop()
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning && _udpClient != null)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref remoteEndPoint);
                    if (data == null || data.Length == 0)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(data);
                    ScadaTelemetryFrame parsed = JsonUtility.FromJson<ScadaTelemetryFrame>(json);

                    if (parsed == null || parsed.equipment == null)
                    {
                        lock (_frameLock)
                        {
                            _parseErrors++;
                        }
                        continue;
                    }

                    lock (_frameLock)
                    {
                        _latestFrame = parsed;
                        _hasUnreadFrame = true;
                        _lastReceivedUtc = DateTime.UtcNow;
                        _packetsReceived++;
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // Expected on timeout or shutdown.
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (_frameLock)
                    {
                        _parseErrors++;
                    }

                    if (_isRunning)
                    {
                        Debug.LogWarning($"[ScadaUdpReceiver] Error parsing UDP frame: {ex.Message}");
                    }
                }
            }
        }
    }
}
