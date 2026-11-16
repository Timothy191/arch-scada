using System.Collections.Frozen;
using Scada.Client;
using Scada.Data.Const;
using Scada.Data.Models;

namespace Scada.Config
{
    /// <summary>
    /// Compatibility configuration wrapper for <see cref="Scada.Client.ConnectionOptions"/> targeting 127.0.0.1:10000 by default.
    /// </summary>
    public class ConnectionOptions : Scada.Client.ConnectionOptions
    {
        public ConnectionOptions()
        {
            Host = "127.0.0.1";
            Port = 10000;
            Timeout = 2000;
        }
    }
}

namespace ArchScada.CoreIntegration
{
    /// <summary>
    /// Native Rapid SCADA binary TCP protocol adapter (<c>ScadaClient</c> on <c>127.0.0.1:10000</c>)
    /// mapping the 34 Arch Coal Preparation Plant (CHPP) channel numbers (<c>Cnl.xml</c>) to HMI tag IDs
    /// with graceful offline/stale fallback.
    /// </summary>
    public sealed class NativeScadaServerAdapter : IDisposable
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 10000;
        public const int DefaultTimeoutMs = 1500;

        private static readonly IReadOnlyDictionary<int, string> DefaultChannelToTagMap =
            new Dictionary<int, string>
            {
                // Line 1 / Device 1: CR-101 Primary Jaw Crusher (ObjNum=2)
                [101] = "CR101_TPH",
                [102] = "CR101_KW",
                [103] = "CR101_BRG",

                // Line 1 / Device 2: SC-102 Vibrating Screen 1 (ObjNum=2)
                [111] = "SC102_VIB",
                [112] = "SC102_KW",

                // Line 1 / Device 3: CV-101 Main Feed Conveyor (ObjNum=2)
                [121] = "CV101_SPD",
                [122] = "CV101_AMP",

                // Line 1 / Device 4: CV-102 Clean Coal Conveyor (ObjNum=2)
                [131] = "CV102_SPD",
                [132] = "CV102_TMP",

                // Line 1 / Device 5: TK-201 High-Rate Thickener (ObjNum=3)
                [201] = "TK201_LVL",
                [202] = "TK201_TRQ",
                [203] = "TK201_TRB",

                // Line 1 / Devices 6-8: PP-201..203 Thickener Pumps (ObjNum=3)
                [211] = "PP201_FLW",
                [212] = "PP201_PRS",
                [221] = "PP202_FLW",
                [231] = "PP203_DOS",

                // Line 1 / Device 9: TK-301 Magnetite & DMC Circuit (ObjNum=4)
                [301] = "TK301_SG",
                [302] = "TK301_LVL",
                [303] = "MS301_REC",
                [304] = "DM301_FLW",
                [305] = "DM301_PRS",

                // Line 2 / Device 12: CF-501 Fine Coal Centrifuge via OPC UA (ObjNum=5)
                [501] = "CF501_RPM",
                [502] = "CF501_VIB",
                [503] = "CF501_TORQ",
                [504] = "CF501_MST",

                // Line 2 / Device 11: AN-601 PGNAA Online Analyzer via OPC UA (ObjNum=5)
                [601] = "AN601_ASH",
                [602] = "AN601_MST",
                [603] = "AN601_SUL",
                [604] = "AN601_CAL",

                // Line 3 / Devices 21-23: MQTT Field Dust, Belt-Rip & Env Sensors (ObjNum=6)
                [701] = "ENV_DUST_PM10",
                [702] = "ENV_DUST_PM25",
                [711] = "CV101_RIP",
                [712] = "CV102_RIP",
                [721] = "ENV_CH4_PPM"
            }.ToFrozenDictionary();

        private static readonly IReadOnlyDictionary<string, int> DefaultTagToChannelMap =
            DefaultChannelToTagMap
                .ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly int[] OrderedChannelNumbers =
            DefaultChannelToTagMap.Keys.OrderBy(c => c).ToArray();

        private readonly object _syncLock = new();
        private readonly ScadaClient _scadaClient;
        private readonly Func<int[], CnlData[]>? _customDataFetcher;
        private readonly Dictionary<string, double> _lastKnownValues = new(StringComparer.OrdinalIgnoreCase);
        private long _cnlListId;

        /// <summary>
        /// Gets the canonical 34-channel mapping from Rapid SCADA <c>CnlNum</c> to <c>TagId</c>.
        /// </summary>
        public static IReadOnlyDictionary<int, string> ChannelToTagMap => DefaultChannelToTagMap;

        /// <summary>
        /// Gets the canonical 34-channel mapping from Rapid SCADA <c>CnlNum</c> to <c>TagId</c>.
        /// </summary>
        public static IReadOnlyDictionary<int, string> ChannelMap => DefaultChannelToTagMap;

        /// <summary>
        /// Gets the reverse mapping from <c>TagId</c> to Rapid SCADA <c>CnlNum</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, int> TagToChannelMap => DefaultTagToChannelMap;

        /// <summary>
        /// Gets the ordered list of 34 CHPP channel numbers configured in <c>Cnl.xml</c>.
        /// </summary>
        public static IReadOnlyList<int> ChannelNumbers => OrderedChannelNumbers;

        /// <summary>
        /// Gets the underlying Rapid SCADA <see cref="Scada.Client.ConnectionOptions"/>.
        /// </summary>
        public Scada.Client.ConnectionOptions ConnectionOptions { get; }

        /// <summary>
        /// Gets the underlying Rapid SCADA binary TCP <see cref="ScadaClient"/>.
        /// </summary>
        public ScadaClient Client => _scadaClient;

        /// <summary>
        /// Gets a value indicating whether the last poll to <c>ScadaServer</c> succeeded.
        /// </summary>
        public bool IsOnline { get; private set; }

        /// <summary>
        /// Gets a value indicating whether <c>ScadaServer</c> is currently unreachable or offline.
        /// </summary>
        public bool IsOffline => !IsOnline;

        /// <summary>
        /// Gets the status message produced by the most recent <see cref="PollChannelTags"/> call.
        /// </summary>
        public string LastStatusMessage { get; private set; } = "Not polled";

        public NativeScadaServerAdapter(
            Scada.Client.ConnectionOptions? connectionOptions = null,
            Func<int[], CnlData[]>? customDataFetcher = null)
        {
            ConnectionOptions = connectionOptions ?? CreateDefaultConnectionOptions();
            if (string.IsNullOrWhiteSpace(ConnectionOptions.Host))
            {
                ConnectionOptions.Host = DefaultHost;
            }

            if (ConnectionOptions.Port <= 0)
            {
                ConnectionOptions.Port = DefaultPort;
            }

            if (ConnectionOptions.Timeout <= 0)
            {
                ConnectionOptions.Timeout = DefaultTimeoutMs;
            }

            _scadaClient = new ScadaClient(ConnectionOptions);
            _customDataFetcher = customDataFetcher;
        }

        public NativeScadaServerAdapter(string host, int port = DefaultPort, int timeoutMs = DefaultTimeoutMs)
            : this(new Scada.Config.ConnectionOptions
            {
                Host = string.IsNullOrWhiteSpace(host) ? DefaultHost : host,
                Port = port > 0 ? port : DefaultPort,
                Timeout = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs
            })
        {
        }

        /// <summary>
        /// Creates the default <see cref="Scada.Config.ConnectionOptions"/> for <c>127.0.0.1:10000</c>.
        /// </summary>
        public static Scada.Config.ConnectionOptions CreateDefaultConnectionOptions() => new()
        {
            Name = "ArchCoalPlant",
            Host = DefaultHost,
            Port = DefaultPort,
            Username = "ScadaWeb",
            Password = "scada",
            Instance = "Default",
            Timeout = DefaultTimeoutMs
        };

        /// <summary>
        /// Resolves a Rapid SCADA channel number (<c>CnlNum</c>) to its corresponding CHPP <c>TagId</c>.
        /// </summary>
        public bool TryGetTagId(int cnlNum, out string tagId)
        {
            if (DefaultChannelToTagMap.TryGetValue(cnlNum, out var found) && found is not null)
            {
                tagId = found;
                return true;
            }

            tagId = string.Empty;
            return false;
        }

        /// <summary>
        /// Resolves a Rapid SCADA channel number (<c>CnlNum</c>) to its <c>TagId</c>, or <c>null</c> if unmapped.
        /// </summary>
        public string? GetTagId(int cnlNum) =>
            DefaultChannelToTagMap.TryGetValue(cnlNum, out var tagId) ? tagId : null;

        /// <summary>
        /// Resolves a CHPP <c>TagId</c> to its Rapid SCADA channel number (<c>CnlNum</c>).
        /// </summary>
        public bool TryGetChannelNumber(string tagId, out int cnlNum)
        {
            if (!string.IsNullOrWhiteSpace(tagId) && DefaultTagToChannelMap.TryGetValue(tagId, out cnlNum))
            {
                return true;
            }

            cnlNum = 0;
            return false;
        }

        /// <summary>
        /// Safely queries <see cref="ScadaClient"/> for the 34 CHPP channels on <c>127.0.0.1:10000</c>.
        /// Never throws unhandled exceptions when <c>ScadaServer</c> is unreachable; instead returns
        /// graceful offline/stale tag snapshots and sets <paramref name="statusMessage"/>.
        /// </summary>
        public IReadOnlyList<TagValueSnapshot> PollChannelTags(out string statusMessage)
        {
            var nowUtc = DateTimeOffset.UtcNow;

            lock (_syncLock)
            {
                try
                {
                    CnlData[] cnlData;
                    if (_customDataFetcher is not null)
                    {
                        cnlData = _customDataFetcher(OrderedChannelNumbers);
                    }
                    else if (_cnlListId > 0)
                    {
                        cnlData = _scadaClient.GetCurrentData(ref _cnlListId);
                        if (cnlData.Length != OrderedChannelNumbers.Length)
                        {
                            cnlData = _scadaClient.GetCurrentData(OrderedChannelNumbers, useCache: true, out _cnlListId);
                        }
                    }
                    else
                    {
                        cnlData = _scadaClient.GetCurrentData(OrderedChannelNumbers, useCache: true, out _cnlListId);
                    }

                    if (cnlData is null || cnlData.Length == 0)
                    {
                        IsOnline = false;
                        statusMessage = $"Offline / Stale: ScadaServer at {ConnectionOptions.Host}:{ConnectionOptions.Port} returned empty channel data.";
                        LastStatusMessage = statusMessage;
                        return BuildStaleSnapshots(nowUtc);
                    }

                    var snapshots = new List<TagValueSnapshot>(OrderedChannelNumbers.Length);
                    var count = Math.Min(OrderedChannelNumbers.Length, cnlData.Length);

                    for (var i = 0; i < count; i++)
                    {
                        var cnlNum = OrderedChannelNumbers[i];
                        var tagId = DefaultChannelToTagMap[cnlNum];
                        var datum = cnlData[i];
                        var quality = MapChannelStatusToQuality(datum);
                        var value = datum.IsDefined && !double.IsNaN(datum.Val)
                            ? datum.Val
                            : (_lastKnownValues.TryGetValue(tagId, out var prev) ? prev : 0.0);

                        if (datum.IsDefined && !double.IsNaN(datum.Val))
                        {
                            _lastKnownValues[tagId] = datum.Val;
                        }

                        snapshots.Add(new TagValueSnapshot
                        {
                            TagId = tagId,
                            Value = value,
                            TimestampUtc = nowUtc,
                            Quality = quality
                        });
                    }

                    IsOnline = true;
                    statusMessage = $"Online: Polled {snapshots.Count} channels from ScadaServer at {ConnectionOptions.Host}:{ConnectionOptions.Port}.";
                    LastStatusMessage = statusMessage;
                    return snapshots;
                }
                catch (Exception ex)
                {
                    IsOnline = false;
                    _cnlListId = 0;

                    try
                    {
                        _scadaClient.Close();
                    }
                    catch
                    {
                        // Ignore secondary socket cleanup errors when server is unreachable.
                    }

                    statusMessage = $"Offline / Stale: ScadaServer unreachable at {ConnectionOptions.Host}:{ConnectionOptions.Port} ({ex.Message})";
                    LastStatusMessage = statusMessage;
                    return BuildStaleSnapshots(nowUtc);
                }
            }
        }

        private IReadOnlyList<TagValueSnapshot> BuildStaleSnapshots(DateTimeOffset timestampUtc)
        {
            var stale = new List<TagValueSnapshot>(OrderedChannelNumbers.Length);
            foreach (var cnlNum in OrderedChannelNumbers)
            {
                var tagId = DefaultChannelToTagMap[cnlNum];
                var lastValue = _lastKnownValues.TryGetValue(tagId, out var prev) ? prev : 0.0;
                stale.Add(new TagValueSnapshot
                {
                    TagId = tagId,
                    Value = lastValue,
                    TimestampUtc = timestampUtc,
                    Quality = "Stale"
                });
            }

            return stale;
        }

        private static string MapChannelStatusToQuality(CnlData datum)
        {
            if (!datum.IsDefined || double.IsNaN(datum.Val))
            {
                return "Stale";
            }

            return datum.Stat switch
            {
                CnlStatusID.FormulaError or CnlStatusID.Error => "Bad",
                CnlStatusID.Unreliable => "Uncertain",
                _ => "Good"
            };
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                try
                {
                    _scadaClient.Close();
                }
                catch
                {
                    // Suppress socket close exceptions during disposal.
                }
            }
        }
    }
}
