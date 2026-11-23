using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace ArchScada.Desktop.Models;

/// <summary>
/// Externalized provisional site engineering parameters loaded from appsettings.json section "ChppEngineering".
/// Parameters remain marked provisional until confirmed against issued-for-construction P&amp;IDs and vendor datasheets.
/// </summary>
public sealed class ChppEngineeringConfig
{
    public const string RoutingBypassToCv104 = "BypassToCv104";
    public const string RoutingDivertThroughDoubleRollCrusher = "DivertThroughDoubleRollCrusher";
    public const string RoutingSplitDiverter50_50 = "SplitDiverter50_50";

    public const string FinesDryThermalBlend = "DryThermalBlend";
    public const string FinesFineCoalSpiralsSp401 = "FineCoalSpiralsSp401";

    private static readonly object SyncLock = new();
    private static ChppEngineeringConfig? _current;

    public bool IsSiteConfirmed { get; set; }
    public string JawDischargeRoutingMode { get; set; } = RoutingBypassToCv104;
    public string DryFinesDestinationMode { get; set; } = FinesDryThermalBlend;
    public double GrizzlyApertureMm { get; set; } = 75.0;
    public double SizingTopDeckApertureMm { get; set; } = 50.0;
    public double SizingBottomDeckApertureMm { get; set; } = 6.0;
    public double DeslimingApertureMm { get; set; } = 0.5;
    public int PrimaryBeltWidthMm { get; set; } = 1200;
    public int SecondaryBeltWidthMm { get; set; } = 900;
    public Dictionary<string, double> RatedMotorKw { get; set; } = CreateDefaultRatedMotorKw();

    public static ChppEngineeringConfig Current
    {
        get
        {
            lock (SyncLock)
            {
                return _current ??= Load();
            }
        }
    }

    public static ChppEngineeringConfig Reload(string? explicitAppSettingsPath = null)
    {
        lock (SyncLock)
        {
            _current = Load(explicitAppSettingsPath);
            return _current;
        }
    }

    public static ChppEngineeringConfig Load(string? explicitAppSettingsPath = null)
    {
        var config = new ChppEngineeringConfig();
        var path = explicitAppSettingsPath ?? FindAppSettingsPath();

        if (path is not null && File.Exists(path))
        {
            var root = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: false)
                .Build();

            root.GetSection("ChppEngineering").Bind(config);
        }

        // Normalize dictionary to case-insensitive and ensure all provisional defaults exist.
        var normalized = new Dictionary<string, double>(CreateDefaultRatedMotorKw(), StringComparer.OrdinalIgnoreCase);
        if (config.RatedMotorKw is not null)
        {
            foreach (var (k, v) in config.RatedMotorKw)
            {
                if (!string.IsNullOrWhiteSpace(k) && v > 0)
                    normalized[k] = v;
            }
        }
        config.RatedMotorKw = normalized;

        if (string.IsNullOrWhiteSpace(config.JawDischargeRoutingMode))
            config.JawDischargeRoutingMode = RoutingBypassToCv104;
        if (string.IsNullOrWhiteSpace(config.DryFinesDestinationMode))
            config.DryFinesDestinationMode = FinesDryThermalBlend;

        return config;
    }

    public double? GetRatedMotorKw(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || RatedMotorKw is null)
            return null;

        if (RatedMotorKw.TryGetValue(nodeId, out var kw))
            return kw;

        foreach (var (k, v) in RatedMotorKw)
        {
            if (string.Equals(k, nodeId, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    public static string? FindAppSettingsPath()
    {
        var directBase = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(directBase))
            return directBase;

        var directCwd = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(directCwd))
            return directCwd;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var desktopCandidate = Path.Combine(dir.FullName, "ArchScada", "ArchScada.Desktop", "appsettings.json");
                if (File.Exists(desktopCandidate))
                    return desktopCandidate;

                var rootCandidate = Path.Combine(dir.FullName, "appsettings.json");
                if (File.Exists(rootCandidate))
                    return rootCandidate;

                dir = dir.Parent;
            }
        }

        return null;
    }

    public static Dictionary<string, double> CreateDefaultRatedMotorKw() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["FEL-101"] = 260.0,
        ["VF-101"] = 30.0,
        ["JC-101"] = 315.0,
        ["DRC-101"] = 160.0,
        ["SS-101"] = 55.0,
        ["DS-201"] = 45.0,
        ["WP-301"] = 315.0,
        ["MS-301"] = 90.0,
        ["TK-301"] = 37.0,
        ["TK-201"] = 110.0,
        ["PP-201"] = 132.0,
        ["PP-202"] = 132.0,
        ["PP-203"] = 18.5,
        ["CV-101"] = 160.0,
        ["CV-102"] = 132.0,
        ["CV-103"] = 75.0,
        ["CV-104"] = 90.0,
        ["CV-105"] = 37.0,
        ["CV-106"] = 75.0,
        ["CV-201"] = 110.0,
        ["CV-202"] = 110.0,
        ["CV-203"] = 90.0,
        ["CV-301"] = 75.0,
        ["CV-302"] = 75.0
    };
}
