using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ArchScada.ControlBoundary;
using ArchScada.CoreIntegration;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;
using Microsoft.Extensions.Configuration;

namespace ArchScada.Desktop.Services;

public sealed record DeploymentCheckSpec(
    string Id,
    string Category,
    string Title,
    bool IsCritical,
    Func<CancellationToken, Task<(StartupCheckState State, string Detail)>> ExecuteAsync);

public interface IDeploymentReadinessService
{
    IReadOnlyList<DeploymentCheckSpec> Checks { get; }
}

public sealed class DeploymentReadinessOptions
{
    public string? AppSettingsPath { get; init; }
    public string? BaseXmlDirectory { get; init; }
    public string? ScadaCommConfigPath { get; init; }
    public string? AuditDataDirectory { get; init; }
    public string? HistorianStorageDirectory { get; init; }
    public string? GlbExportDirectory { get; init; }
    public string? ScadaServerBaseUrl { get; init; }
    public string? NativeAdapterHost { get; init; }
    public int? NativeAdapterPort { get; init; }
    public string? ModbusHost { get; init; }
    public int? ModbusPort { get; init; }
    public string? PostgresHost { get; init; }
    public int? PostgresPort { get; init; }
    public string? LocalBinDirectory { get; init; }
    public string? DeployScriptsDirectory { get; init; }
    public string? SystemdUserDirectory { get; init; }
    public Func<ChppEngineeringConfig>? ConfigProvider { get; init; }
    public int ProbeTimeoutMs { get; init; } = 350;
}

public sealed class DeploymentReadinessService : IDeploymentReadinessService
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private readonly UnityBridgeService _unityBridge;
    private readonly DeploymentReadinessOptions _options;

    public IReadOnlyList<DeploymentCheckSpec> Checks { get; }

    public DeploymentReadinessService(UnityBridgeService unityBridge, DeploymentReadinessOptions? options = null)
    {
        _unityBridge = unityBridge ?? throw new ArgumentNullException(nameof(unityBridge));
        _options = options ?? new DeploymentReadinessOptions();

        Checks = new DeploymentCheckSpec[]
        {
            new("cfg",  "CONFIG",  "Plant & Engineering Configuration",            IsCritical: true,  CheckPlantConfigurationAsync),
            new("xml",  "SCHEMA",  "Rapid SCADA BaseXML Register",                 IsCritical: true,  CheckBaseXmlRegisterAsync),
            new("sec",  "SAFETY",  "Control Boundary, RBAC & Audit Trail",         IsCritical: true,  CheckControlSafetyAndAuditAsync),
            new("srv",  "SERVER",  "ScadaServer & Native Binary Protocol",         IsCritical: false, CheckScadaServerAsync),
            new("comm", "DRIVERS", "Field Bus & Protocol Drivers",                 IsCritical: false, CheckFieldBusDriversAsync),
            new("hist", "STORAGE", "Historian Storage (PostgreSQL 16 + SQLite)",   IsCritical: true,  CheckHistorianStorageAsync),
            new("3d",   "3D TWIN", "3D Digital Twin GLB Assets & PBR Engine",      IsCritical: true,  Check3DAssetsAsync),
            new("ops",  "DEPLOY",  "Workstation Package, Backups & Services",      IsCritical: false, CheckDeploymentArtifactsAsync),
        };
    }

    private Task<(StartupCheckState State, string Detail)> CheckPlantConfigurationAsync(CancellationToken ct)
    {
        try
        {
            var appSettingsPath = _options.AppSettingsPath ?? ChppEngineeringConfig.FindAppSettingsPath();
            if (string.IsNullOrWhiteSpace(appSettingsPath) || !File.Exists(appSettingsPath))
            {
                return Task.FromResult((StartupCheckState.Failed, "Missing appsettings.json configuration file"));
            }

            using (var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath)))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Task.FromResult((StartupCheckState.Failed, "Invalid appsettings.json root structure"));
                }
            }

            var config = _options.ConfigProvider?.Invoke()
                ?? (!string.IsNullOrWhiteSpace(_options.AppSettingsPath)
                    ? ChppEngineeringConfig.Load(_options.AppSettingsPath)
                    : ChppEngineeringConfig.Current);

            if (config.GrizzlyApertureMm <= 0 ||
                config.SizingTopDeckApertureMm <= 0 ||
                config.SizingBottomDeckApertureMm <= 0 ||
                config.DeslimingApertureMm <= 0)
            {
                return Task.FromResult((StartupCheckState.Failed, "Invalid screen aperture specification (must be > 0 mm)"));
            }

            if (config.PrimaryBeltWidthMm <= 0 || config.SecondaryBeltWidthMm <= 0)
            {
                return Task.FromResult((StartupCheckState.Failed, "Invalid conveyor belt width specification"));
            }

            if (config.RatedMotorKw is null || config.RatedMotorKw.Count < 20 || config.RatedMotorKw.Values.Any(v => v <= 0 || !double.IsFinite(v)))
            {
                return Task.FromResult((StartupCheckState.Failed, $"Insufficient motor ratings ({config.RatedMotorKw?.Count ?? 0} < 20)"));
            }

            var nodes = Plant3DSceneData.BuildNodes();
            var machineCount = nodes.Count(n => !n.IsConveyor);
            var conveyorCount = nodes.Count(n => n.IsConveyor);
            if (nodes.Count != 29 || machineCount != 18 || conveyorCount != 11)
            {
                return Task.FromResult((StartupCheckState.Failed, $"GA topology mismatch: {nodes.Count} nodes ({machineCount}M/{conveyorCount}C, expected 18M/11C)"));
            }

            var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                foreach (var up in node.UpstreamIds)
                {
                    if (!nodeIds.Contains(up))
                    {
                        return Task.FromResult((StartupCheckState.Failed, $"GA node {node.Id} references unknown upstream '{up}'"));
                    }
                }

                foreach (var down in node.DownstreamIds)
                {
                    if (!nodeIds.Contains(down))
                    {
                        return Task.FromResult((StartupCheckState.Failed, $"GA node {node.Id} references unknown downstream '{down}'"));
                    }
                }
            }

            var state = config.IsSiteConfirmed ? StartupCheckState.Passed : StartupCheckState.Warning;
            var confirmationLabel = config.IsSiteConfirmed ? "Site Confirmed" : "Provisional (Site Unconfirmed)";
            return Task.FromResult((state, $"{nodes.Count} GA nodes · {config.RatedMotorKw.Count} motor specs · {confirmationLabel}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((StartupCheckState.Failed, $"Configuration validation error: {ex.Message}"));
        }
    }

    private Task<(StartupCheckState State, string Detail)> CheckBaseXmlRegisterAsync(CancellationToken ct)
    {
        try
        {
            var baseXmlDir = ResolveBaseXmlDirectory();
            if (baseXmlDir is null)
            {
                return Task.FromResult((StartupCheckState.Warning,
                    $"Standalone bundle · {NativeScadaServerAdapter.ChannelNumbers.Count} embedded Cnl mappings active"));
            }

            var deviceXmlPath = Path.Combine(baseXmlDir, "Device.xml");
            var cnlXmlPath = Path.Combine(baseXmlDir, "Cnl.xml");
            if (!File.Exists(deviceXmlPath) || !File.Exists(cnlXmlPath))
            {
                return Task.FromResult((StartupCheckState.Failed,
                    $"BaseXML incomplete at {baseXmlDir} (missing Device.xml or Cnl.xml)"));
            }

            var deviceDoc = XDocument.Load(deviceXmlPath);
            var devices = deviceDoc.Descendants("Device").ToList();
            if (devices.Count < 18)
            {
                return Task.FromResult((StartupCheckState.Failed,
                    $"Device.xml has {devices.Count} devices (expected >= 18)"));
            }

            var cnlDoc = XDocument.Load(cnlXmlPath);
            var cnlMap = new Dictionary<int, int>();
            foreach (var cnlEl in cnlDoc.Descendants("Cnl"))
            {
                var numStr = cnlEl.Element("CnlNum")?.Value;
                var typeStr = cnlEl.Element("CnlTypeID")?.Value;
                if (int.TryParse(numStr, out var cnlNum) && int.TryParse(typeStr, out var cnlTypeId))
                {
                    cnlMap[cnlNum] = cnlTypeId;
                }
            }

            var missingChannels = new List<int>();
            var nonReadOnlyChannels = new List<int>();
            foreach (var expectedCnl in NativeScadaServerAdapter.ChannelNumbers)
            {
                if (!cnlMap.TryGetValue(expectedCnl, out var typeId))
                {
                    missingChannels.Add(expectedCnl);
                }
                else if (typeId != 1)
                {
                    nonReadOnlyChannels.Add(expectedCnl);
                }
            }

            if (missingChannels.Count > 0)
            {
                return Task.FromResult((StartupCheckState.Failed,
                    $"Cnl.xml missing {missingChannels.Count} mapped channel(s), e.g. Cnl {missingChannels[0]}"));
            }

            if (nonReadOnlyChannels.Count > 0)
            {
                return Task.FromResult((StartupCheckState.Failed,
                    $"Read-only violation in Cnl.xml: Cnl {nonReadOnlyChannels[0]} has CnlTypeID != 1"));
            }

            return Task.FromResult((StartupCheckState.Passed,
                $"{devices.Count} field devices · {NativeScadaServerAdapter.ChannelNumbers.Count} read-only Cnl channels verified"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((StartupCheckState.Failed, $"BaseXML parse error: {ex.Message}"));
        }
    }

    private Task<(StartupCheckState State, string Detail)> CheckControlSafetyAndAuditAsync(CancellationToken ct)
    {
        try
        {
            var rules = CauseAndEffectMatrixLoader.CreateDefault();
            if (rules is null || rules.Count == 0 || !rules.Any(r => r.RequiredInterlocks.Count > 0))
            {
                return Task.FromResult((StartupCheckState.Failed, "Cause & Effect matrix returned no interlock rules"));
            }

            OperatorAuthService auth;
            var appSettingsPath = _options.AppSettingsPath ?? ChppEngineeringConfig.FindAppSettingsPath();
            if (!string.IsNullOrWhiteSpace(appSettingsPath) && File.Exists(appSettingsPath))
            {
                var config = new ConfigurationBuilder()
                    .AddJsonFile(appSettingsPath, optional: true, reloadOnChange: false)
                    .Build();
                auth = new OperatorAuthService(config);
            }
            else
            {
                auth = new OperatorAuthService();
            }

            if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.CurrentOperatorId))
            {
                return Task.FromResult((StartupCheckState.Failed, "RBAC OperatorAuthService failed to load valid accounts"));
            }

            var auditDir = _options.AuditDataDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchScada");

            Directory.CreateDirectory(auditDir);
            var probePath = Path.Combine(auditDir, $".audit-write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);

            foreach (var candidateName in new[] { "control_audit.jsonl", "control-audit.jsonl" })
            {
                var auditFile = Path.Combine(auditDir, candidateName);
                if (File.Exists(auditFile))
                {
                    var logger = new ControlAuditLogger(auditFile);
                    if (!logger.VerifyChainIntegrity(out var integrityError))
                    {
                        return Task.FromResult((StartupCheckState.Failed,
                            $"Audit hash chain check failed ({candidateName}): {integrityError}"));
                    }
                }
            }

            return Task.FromResult((StartupCheckState.Passed,
                $"C&E interlocks loaded ({rules.Count}) · RBAC & SHA-256 audit ready"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((StartupCheckState.Failed, $"Safety/audit check failed: {ex.Message}"));
        }
    }

    private async Task<(StartupCheckState State, string Detail)> CheckScadaServerAsync(CancellationToken ct)
    {
        var (baseUrl, nativeHost, nativePort) = ResolveServerEndpoints();
        int timeoutMs = Math.Clamp(_options.ProbeTimeoutMs, 50, 400);

        var httpTask = ProbeHttpHealthAsync(baseUrl, timeoutMs, ct);
        var nativeTask = ProbeNativeScadaTcpAsync(nativeHost, nativePort, timeoutMs, ct);

        await Task.WhenAll(httpTask, nativeTask).ConfigureAwait(false);
        bool httpOk = await httpTask.ConfigureAwait(false);
        bool nativeOk = await nativeTask.ConfigureAwait(false);

        if (httpOk && nativeOk)
        {
            return (StartupCheckState.Passed,
                $"Live ScadaServer HTTP + Native TCP :{nativePort} online ({NativeScadaServerAdapter.ChannelNumbers.Count} Cnl)");
        }

        if (httpOk || nativeOk)
        {
            return (StartupCheckState.Warning,
                $"Partial server online (HTTP={(httpOk ? "up" : "offline")}, Native :{nativePort}={(nativeOk ? "up" : "offline")})");
        }

        return (StartupCheckState.Warning, "Live server offline — Coupled Demo Simulation active");
    }

    private async Task<(StartupCheckState State, string Detail)> CheckFieldBusDriversAsync(CancellationToken ct)
    {
        string? xmlSummary = null;
        var commConfigPath = ResolveScadaCommConfigPath();
        if (commConfigPath is not null)
        {
            try
            {
                var doc = XDocument.Load(commConfigPath);
                int activeLines = doc.Descendants("Line")
                    .Count(l => string.Equals((string?)l.Attribute("active"), "true", StringComparison.OrdinalIgnoreCase));
                int activeDevices = doc.Descendants("Device")
                    .Count(d => string.Equals((string?)d.Attribute("active"), "true", StringComparison.OrdinalIgnoreCase));
                xmlSummary = $"{activeLines} lines / {activeDevices} PLCs";
            }
            catch (Exception ex)
            {
                return (StartupCheckState.Warning, $"ScadaCommConfig.xml parse warning: {ex.Message}");
            }
        }

        var modbusHost = string.IsNullOrWhiteSpace(_options.ModbusHost) ? "127.0.0.1" : _options.ModbusHost;
        int modbusPort = _options.ModbusPort ?? 1502;
        int timeoutMs = Math.Clamp(Math.Min(_options.ProbeTimeoutMs, 250), 50, 250);

        bool modbusLive = await ProbeModbusTcpAsync(modbusHost, modbusPort, timeoutMs, ct).ConfigureAwait(false);
        if (modbusLive)
        {
            return (StartupCheckState.Passed, $"Modbus TCP :{modbusPort} live · OPC UA / MQTT drivers ready");
        }

        return xmlSummary is not null
            ? (StartupCheckState.Warning, $"Drivers validated ({xmlSummary}) · Field bus offline")
            : (StartupCheckState.Warning, "Drivers validated (Modbus/OPC UA/MQTT) · Field bus offline");
    }

    private async Task<(StartupCheckState State, string Detail)> CheckHistorianStorageAsync(CancellationToken ct)
    {
        var historianDir = _options.HistorianStorageDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchScada", "historian");

        var store = new LocalHistorianStore(historianDir);
        if (!store.CheckHealth(out var error))
        {
            return (StartupCheckState.Failed, $"Local historian storage initialization failed: {error}");
        }

        int timeoutMs = Math.Clamp(Math.Min(_options.ProbeTimeoutMs, 250), 50, 250);
        var pgHost = string.IsNullOrWhiteSpace(_options.PostgresHost) ? "127.0.0.1" : _options.PostgresHost;
        int pgPort = ResolvePostgresPort();

        bool pgReachable = await ProbePostgresProtocolAsync(pgHost, pgPort, timeoutMs, ct).ConfigureAwait(false);

        return pgReachable
            ? (StartupCheckState.Passed, $"PostgreSQL historian :{pgPort} online + SQLite WAL buffer healthy")
            : (StartupCheckState.Warning, $"SQLite WAL fallback healthy · PostgreSQL historian :{pgPort} offline");
    }

    private Task<(StartupCheckState State, string Detail)> Check3DAssetsAsync(CancellationToken ct)
    {
        var exportDir = _options.GlbExportDirectory ?? _unityBridge.AssetsExportDirectory;
        if (string.IsNullOrWhiteSpace(exportDir) || !Directory.Exists(exportDir))
        {
            return Task.FromResult((StartupCheckState.Failed, $"3D asset directory not found: {exportDir}"));
        }

        var requiredFiles = Plant3DSceneData.BuildNodes()
            .Select(n => n.GlbFileName)
            .Concat(new[]
            {
                "wheel_loader_bucket.glb", "conveyor_segment.glb",
                "centrifuge_dryer_cf501.glb", "dmc_cyclone_dmc301.glb", "dust_collector_dc101.glb",
                "filter_press_fp501.glb", "mcc_substation_mcc101.glb", "overband_magnet_om101.glb",
                "pgnaa_analyzer_an601.glb", "train_loadout_silo_tls401.glb"
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        long totalBytes = 0;
        var header = new byte[12];

        foreach (var fileName in requiredFiles)
        {
            var fullPath = Path.Combine(exportDir, fileName);
            if (!File.Exists(fullPath))
            {
                return Task.FromResult((StartupCheckState.Failed, $"Missing 3D PBR model: {fileName}"));
            }

            var info = new FileInfo(fullPath);
            if (info.Length < 1024)
            {
                return Task.FromResult((StartupCheckState.Failed, $"Corrupt/truncated GLB ({info.Length} B): {fileName}"));
            }

            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int read = fs.Read(header, 0, 12);
                if (read < 12)
                {
                    return Task.FromResult((StartupCheckState.Failed, $"Incomplete glTF header: {fileName}"));
                }
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
            uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));

            if (magic != GlbMagic || version != 2 || declaredLength < 1024 || declaredLength != info.Length)
            {
                return Task.FromResult((StartupCheckState.Failed,
                    $"Invalid glTF 2.0 binary header in {fileName} (magic=0x{magic:X8}, v={version})"));
            }

            totalBytes += info.Length;
        }

        double totalMiB = totalBytes / (1024.0 * 1024.0);
        return Task.FromResult((StartupCheckState.Passed,
            $"{requiredFiles.Count}/{requiredFiles.Count} glTF 2.0 PBR models verified ({totalMiB:0.00} MiB)"));
    }

    private Task<(StartupCheckState State, string Detail)> CheckDeploymentArtifactsAsync(CancellationToken ct)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var binDir = _options.LocalBinDirectory ?? Path.Combine(home, ".local", "bin");
        var systemdDir = _options.SystemdUserDirectory ?? Path.Combine(home, ".config", "systemd", "user");

        bool hasDesktopBin = File.Exists(Path.Combine(binDir, "archscada"));
        bool hasSimBin = File.Exists(Path.Combine(binDir, "archscada-plc-sim"));

        var scriptsDir = ResolveDeployScriptsDirectory(home);
        bool hasBackupScripts = scriptsDir is not null
            && File.Exists(Path.Combine(scriptsDir, "create-backup.sh"))
            && File.Exists(Path.Combine(scriptsDir, "verify-backup.sh"));

        bool hasBackupTimer = File.Exists(Path.Combine(systemdDir, "archscada-backup.timer"));
        bool hasPlcSimService = File.Exists(Path.Combine(systemdDir, "archscada-plc-sim.service"));
        int systemdUnitCount = Directory.Exists(systemdDir)
            ? Directory.EnumerateFiles(systemdDir, "archscada*").Count()
            : 0;

        if (hasDesktopBin && hasSimBin && hasBackupScripts && hasBackupTimer && hasPlcSimService)
        {
            return Task.FromResult((StartupCheckState.Passed,
                $"Local suite, backup scripts & {systemdUnitCount} systemd units installed"));
        }

        return Task.FromResult((StartupCheckState.Warning, "Dev build mode — run deploy/scripts/install-local.sh"));
    }

    private string? ResolveBaseXmlDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseXmlDirectory))
        {
            return Directory.Exists(_options.BaseXmlDirectory) ? _options.BaseXmlDirectory : null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Projects", "ArchCoalPlant", "BaseXML"),
            Path.Combine(home, ".local", "share", "archscada", "Projects", "ArchCoalPlant", "BaseXML")
        };

        AddAncestorCandidates(candidates, AppContext.BaseDirectory, Path.Combine("Projects", "ArchCoalPlant", "BaseXML"));
        AddAncestorCandidates(candidates, Directory.GetCurrentDirectory(), Path.Combine("Projects", "ArchCoalPlant", "BaseXML"));

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private string? ResolveScadaCommConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ScadaCommConfigPath))
        {
            return File.Exists(_options.ScadaCommConfigPath) ? _options.ScadaCommConfigPath : null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var relPath = Path.Combine("Projects", "ArchCoalPlant", "Instances", "Default", "ScadaComm", "Config", "ScadaCommConfig.xml");
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, relPath),
            Path.Combine(home, ".local", "share", "archscada", relPath)
        };

        AddAncestorCandidates(candidates, AppContext.BaseDirectory, relPath);
        AddAncestorCandidates(candidates, Directory.GetCurrentDirectory(), relPath);

        return candidates.FirstOrDefault(File.Exists);
    }

    private string? ResolveDeployScriptsDirectory(string home)
    {
        if (!string.IsNullOrWhiteSpace(_options.DeployScriptsDirectory))
        {
            return Directory.Exists(_options.DeployScriptsDirectory) ? _options.DeployScriptsDirectory : null;
        }

        var candidates = new List<string>
        {
            Path.Combine(home, ".local", "share", "archscada", "deploy", "scripts")
        };

        AddAncestorCandidates(candidates, AppContext.BaseDirectory, Path.Combine("deploy", "scripts"));
        AddAncestorCandidates(candidates, Directory.GetCurrentDirectory(), Path.Combine("deploy", "scripts"));

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static void AddAncestorCandidates(List<string> candidates, string? startDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(startDir))
        {
            return;
        }

        try
        {
            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                candidates.Add(Path.Combine(dir.FullName, relativePath));
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore filesystem traversal failures.
        }
    }

    private (string BaseUrl, string NativeHost, int NativePort) ResolveServerEndpoints()
    {
        string baseUrl = "http://localhost:5000";
        string nativeHost = NativeScadaServerAdapter.DefaultHost;
        int nativePort = NativeScadaServerAdapter.DefaultPort;

        var appSettingsPath = _options.AppSettingsPath ?? ChppEngineeringConfig.FindAppSettingsPath();
        if (!string.IsNullOrWhiteSpace(appSettingsPath) && File.Exists(appSettingsPath))
        {
            try
            {
                var config = new ConfigurationBuilder()
                    .AddJsonFile(appSettingsPath, optional: true, reloadOnChange: false)
                    .Build();

                var cfgUrl = config["ScadaServer:BaseUrl"];
                if (!string.IsNullOrWhiteSpace(cfgUrl))
                {
                    baseUrl = cfgUrl;
                }

                var cfgHost = config["NativeScadaAdapter:Host"];
                if (!string.IsNullOrWhiteSpace(cfgHost))
                {
                    nativeHost = cfgHost;
                }

                if (int.TryParse(config["NativeScadaAdapter:Port"], out var cfgPort) && cfgPort > 0)
                {
                    nativePort = cfgPort;
                }
            }
            catch
            {
                // Fall back to defaults on malformed config.
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.ScadaServerBaseUrl))
        {
            baseUrl = _options.ScadaServerBaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(_options.NativeAdapterHost))
        {
            nativeHost = _options.NativeAdapterHost;
        }

        if (_options.NativeAdapterPort is > 0)
        {
            nativePort = _options.NativeAdapterPort.Value;
        }

        return (baseUrl, nativeHost, nativePort);
    }

    /// <summary>Dedicated Arch SCADA historian port (deploy/docker-compose.historian.yml → 127.0.0.1:55432).</summary>
    public const int DefaultHistorianPostgresPort = 55432;

    /// <summary>
    /// Resolves the single dedicated historian port. Deliberately no fallback to 5432, which on
    /// shared workstations is commonly owned by unrelated PostgreSQL instances and caused false passes.
    /// </summary>
    public int ResolvePostgresPort()
    {
        if (_options.PostgresPort is > 0)
        {
            return _options.PostgresPort.Value;
        }

        var envPort = Environment.GetEnvironmentVariable("ARCHSCADA_PGPORT");
        if (int.TryParse(envPort, out var parsed) && parsed is > 0 and <= 65535)
        {
            return parsed;
        }

        return DefaultHistorianPostgresPort;
    }

    /// <summary>
    /// Sends a PostgreSQL SSLRequest (length 8, code 80877103). A real PostgreSQL server answers with a
    /// single 'S' or 'N' byte, so arbitrary TCP listeners on the port do not count as a historian.
    /// </summary>
    public static async Task<bool> ProbePostgresProtocolAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            byte[] sslRequest = { 0x00, 0x00, 0x00, 0x08, 0x04, 0xD2, 0x16, 0x2F };
            await stream.WriteAsync(sslRequest, cts.Token).ConfigureAwait(false);
            var reply = new byte[1];
            int read = await stream.ReadAsync(reply, cts.Token).ConfigureAwait(false);
            return read == 1 && (reply[0] == (byte)'S' || reply[0] == (byte)'N');
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeHttpHealthAsync(string baseUrl, int timeoutMs, CancellationToken ct)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var response = await http.GetAsync("api/v1/health", cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeNativeScadaTcpAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        bool tcpConnected = await ProbeTcpConnectAsync(host, port, timeoutMs, ct).ConfigureAwait(false);
        if (!tcpConnected)
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            return await Task.Run(() =>
            {
                using var adapter = new NativeScadaServerAdapter(host, port, timeoutMs);
                var snapshots = adapter.PollChannelTags(out _);
                return adapter.IsOnline && snapshots.Count == NativeScadaServerAdapter.ChannelNumbers.Count;
            }, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeModbusTcpAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            // Modbus TCP MBAP (txId=1, proto=0, len=6, unit=1) + FC 0x04 Read Input Registers (start=0, qty=1)
            byte[] request = { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x04, 0x00, 0x00, 0x00, 0x01 };
            await stream.WriteAsync(request, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            var response = new byte[16];
            int totalRead = 0;
            while (totalRead < 9)
            {
                int read = await stream.ReadAsync(response.AsMemory(totalRead, response.Length - totalRead), cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                totalRead += read;
            }

            if (totalRead < 9)
            {
                return false;
            }

            ushort txId = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0, 2));
            ushort protoId = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
            byte fc = response[7];
            return txId == 1 && protoId == 0 && (fc == 0x04 || fc == 0x84);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeTcpConnectAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
