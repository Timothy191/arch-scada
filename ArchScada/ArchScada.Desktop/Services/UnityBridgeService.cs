using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Services;

public sealed class UnityBridgeService : IDisposable
{
    private readonly UdpClient _udp = new();
    private readonly object _udpLock = new();
    private readonly string _host = "127.0.0.1";
    private readonly int _port = 17850;
    private bool _disposed;

    public bool IsBlenderInstalled { get; }
    public bool IsUnityHubInstalled { get; }
    public string AssetsExportDirectory { get; }
    public string BlenderScriptPath { get; }
    public bool IsStreaming { get; private set; }
    public int PacketsSent { get; private set; }
    public string Endpoint => $"udp://{_host}:{_port}";

    public UnityBridgeService()
    {
        IsBlenderInstalled = File.Exists("/usr/bin/blender") || CommandExists("blender");
        IsUnityHubInstalled = File.Exists("/usr/bin/unityhub") || CommandExists("unityhub");

        var baseDir = AppContext.BaseDirectory;
        var bundledExportDir = Path.Combine(baseDir, "Assets", "3D", "Exported");
        var bundledBlenderScript = Path.Combine(baseDir, "Assets", "3D", "Blender", "generate_plant_assets.py");
        var repoRoot = FindRepoRoot(baseDir) ?? FindRepoRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();

        AssetsExportDirectory = Directory.Exists(bundledExportDir)
            ? bundledExportDir
            : Path.Combine(repoRoot, "ArchScada", "Assets", "3D", "Exported");
        BlenderScriptPath = File.Exists(bundledBlenderScript)
            ? bundledBlenderScript
            : Path.Combine(repoRoot, "ArchScada", "Assets", "3D", "Blender", "generate_plant_assets.py");
    }

    public IReadOnlyList<string> GetExportedModels()
    {
        if (!Directory.Exists(AssetsExportDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(AssetsExportDirectory)
            .Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToList();
    }

    public void PublishFrame(IEnumerable<EquipmentItem> equipment, Unity3DEffectSettings fx)
    {
        if (equipment is null || fx is null)
        {
            IsStreaming = false;
            return;
        }

        try
        {
            static double FiniteOrZero(double v) => double.IsFinite(v) ? v : 0.0;

            var payload = new
            {
                timestampUtc = DateTime.UtcNow,
                readOnly = true,
                effects = new
                {
                    fx.CameraPreset,
                    fx.ShowFlowParticles,
                    fx.ShowThermalHeatmap,
                    fx.ShowAlarmHalos,
                    fx.AnimateConveyors
                },
                equipment = equipment
                    .Where(e => e is not null)
                    .Select(e => new
                    {
                        e.Id,
                        e.Area,
                        e.Type,
                        status = e.Status.ToString(),
                        PowerKw = FiniteOrZero(e.PowerKw),
                        LoadPercent = FiniteOrZero(e.LoadPercent),
                        PrimaryMetric = FiniteOrZero(e.PrimaryMetric),
                        e.PrimaryMetricUnit
                    })
            };

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            lock (_udpLock)
            {
                if (_disposed)
                {
                    IsStreaming = false;
                    return;
                }
                _udp.Send(bytes, bytes.Length, _host, _port);
                IsStreaming = true;
                PacketsSent++;
            }
        }
        catch
        {
            IsStreaming = false;
        }
    }

    public async Task<(bool Success, string Output)> GenerateBlenderAssetsAsync()
    {
        if (!IsBlenderInstalled)
        {
            return (false, "Blender binary not found on PATH.");
        }

        if (!File.Exists(BlenderScriptPath))
        {
            return (false, $"Blender generator script not found: {BlenderScriptPath}");
        }

        Directory.CreateDirectory(AssetsExportDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = "blender",
            Arguments = $"--background --python \"{BlenderScriptPath}\" -- \"{AssetsExportDirectory}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return (false, "Failed to start Blender process.");
        }

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode == 0)
        {
            var optNote = await TryOptimizeExportedGlbsAsync();
            return (true, string.IsNullOrEmpty(optNote)
                ? $"Generated 3D assets in {AssetsExportDirectory}"
                : $"Generated & optimized 3D assets in {AssetsExportDirectory} ({optNote})");
        }

        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private async Task<string?> TryOptimizeExportedGlbsAsync()
    {
        try
        {
            if (!CommandExists("node"))
                return null;

            var blenderDir = Path.GetDirectoryName(BlenderScriptPath);
            var assetRoot = blenderDir is null ? null : Path.GetDirectoryName(blenderDir);
            if (assetRoot is null)
                return null;

            var optScript = Path.Combine(assetRoot, "optimize_glbs.mjs");
            var nodeModules = Path.Combine(assetRoot, "node_modules");
            if (!File.Exists(optScript) || !Directory.Exists(nodeModules))
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{optScript}\" \"{AssetsExportDirectory}\"",
                WorkingDirectory = assetRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var optProc = Process.Start(psi);
            if (optProc is null)
                return null;

            var optOut = await optProc.StandardOutput.ReadToEndAsync();
            await optProc.WaitForExitAsync();
            if (optProc.ExitCode != 0)
                return null;

            var lastLine = optOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            return string.IsNullOrEmpty(lastLine) ? "glTF-Transform optimized" : lastLine;
        }
        catch
        {
            return null;
        }
    }

    private static bool CommandExists(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo("which", cmd)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1500);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "ArchScada")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    public void Dispose()
    {
        lock (_udpLock)
        {
            if (_disposed) return;
            _disposed = true;
            IsStreaming = false;
            _udp.Dispose();
        }
    }
}
