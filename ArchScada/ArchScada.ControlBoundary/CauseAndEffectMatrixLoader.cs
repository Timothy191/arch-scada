using System.Text.Json;

namespace ArchScada.ControlBoundary;

/// <summary>
/// Loads, validates, and provides approved Cause-and-Effect matrix rules across CHPP circuits.
/// Supports loading from version-controlled JSON with built-in fallbacks for isolated test environments.
/// </summary>
public static class CauseAndEffectMatrixLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads the Cause-and-Effect matrix from disk, falling back to built-in defaults if absent or unreadable.
    /// </summary>
    /// <param name="filePath">Optional custom path to the CauseAndEffectMatrix.json file.</param>
    /// <returns>A validated list of approved CauseAndEffectRule entries.</returns>
    public static IReadOnlyList<CauseAndEffectRule> LoadMatrix(string? filePath = null)
    {
        if (TryLoadMatrix(filePath, out var rules, out _))
        {
            return rules;
        }

        return GetDefaultMatrix();
    }

    /// <summary>
    /// Attempts to load and validate the Cause-and-Effect matrix from a file.
    /// </summary>
    /// <param name="filePath">Optional custom path.</param>
    /// <param name="rules">Loaded rules if successful, or fallback defaults if failed.</param>
    /// <param name="errorMessage">Error description if loading failed.</param>
    /// <returns>True if loaded successfully from file; otherwise false.</returns>
    public static bool TryLoadMatrix(
        string? filePath,
        out IReadOnlyList<CauseAndEffectRule> rules,
        out string? errorMessage)
    {
        string? resolvedPath = ResolveMatrixPath(filePath);
        if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
        {
            errorMessage = $"CauseAndEffectMatrix file not found. Searched path: {filePath ?? "(default locations)"}";
            rules = GetDefaultMatrix();
            return false;
        }

        try
        {
            string json = File.ReadAllText(resolvedPath);
            rules = LoadFromJson(json);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error parsing CauseAndEffectMatrix from '{resolvedPath}': {ex.Message}";
            rules = GetDefaultMatrix();
            return false;
        }
    }

    /// <summary>
    /// Parses and validates Cause-and-Effect rules from a JSON string.
    /// Supports both root object with 'rules' array and direct JSON array.
    /// </summary>
    public static IReadOnlyList<CauseAndEffectRule> LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        List<CauseAndEffectRule>? parsedRules = null;

        // Try parsing as document object with "rules" property
        try
        {
            var doc = JsonSerializer.Deserialize<CauseAndEffectDocument>(json, JsonOptions);
            if (doc?.Rules != null && doc.Rules.Count > 0)
            {
                parsedRules = doc.Rules;
            }
        }
        catch
        {
            // Fall back to direct array deserialization below
        }

        // Try parsing directly as an array of rules if document deserialization produced no rules
        if (parsedRules is null)
        {
            parsedRules = JsonSerializer.Deserialize<List<CauseAndEffectRule>>(json, JsonOptions);
        }

        if (parsedRules is null || parsedRules.Count == 0)
        {
            throw new InvalidOperationException("No valid CauseAndEffectRule entries found in JSON.");
        }

        var validated = new List<CauseAndEffectRule>();
        foreach (var rule in parsedRules)
        {
            if (rule != null && ValidateRule(rule, out _))
            {
                validated.Add(rule);
            }
        }

        if (validated.Count == 0)
        {
            throw new InvalidOperationException("All CauseAndEffectRule entries failed validation.");
        }

        return validated.AsReadOnly();
    }

    /// <summary>
    /// Validates an individual CauseAndEffectRule for structural and safety correctness.
    /// </summary>
    public static bool ValidateRule(CauseAndEffectRule? rule, out string? error)
    {
        if (rule is null)
        {
            error = "Rule is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.RuleId))
        {
            error = "RuleId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.EquipmentId))
        {
            error = $"EquipmentId is required for rule '{rule.RuleId}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.CommandType))
        {
            error = $"CommandType is required for rule '{rule.RuleId}'.";
            return false;
        }

        if (rule.MinParameter.HasValue && rule.MaxParameter.HasValue &&
            rule.MinParameter.Value > rule.MaxParameter.Value)
        {
            error = $"Rule '{rule.RuleId}' has MinParameter ({rule.MinParameter}) greater than MaxParameter ({rule.MaxParameter}).";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Resolves the filesystem path to CauseAndEffectMatrix.json searching common application and repository locations.
    /// </summary>
    public static string? ResolveMatrixPath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        var candidates = new List<string>();

        string? envPath = Environment.GetEnvironmentVariable("ARCHSCADA_CE_MATRIX_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
            candidates.Add(envPath);

        string? appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArchScada",
            "CauseAndEffectMatrix.json");
        candidates.Add(appDataPath);

        string baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Config", "CauseAndEffectMatrix.json"));
        candidates.Add(Path.Combine(baseDir, "CauseAndEffectMatrix.json"));

        try
        {
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                candidates.Add(Path.Combine(dir.FullName, "ArchScada", "Config", "CauseAndEffectMatrix.json"));
                candidates.Add(Path.Combine(dir.FullName, "Config", "CauseAndEffectMatrix.json"));
                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore directory walk errors
        }

        string currentDir = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(currentDir, "ArchScada", "Config", "CauseAndEffectMatrix.json"));
        candidates.Add(Path.Combine(currentDir, "Config", "CauseAndEffectMatrix.json"));
        candidates.Add(Path.Combine(currentDir, "CauseAndEffectMatrix.json"));

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore path format errors
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the built-in fallback Cause-and-Effect matrix covering equipment across all 6 CHPP circuits.
    /// </summary>
    public static IReadOnlyList<CauseAndEffectRule> GetDefaultMatrix()
    {
        return
        [
            // Circuit 1: Crushing & Conveyors (CR-101, SC-102, CV-101, CV-102)
            new CauseAndEffectRule
            {
                RuleId = "CE-SIM-001",
                EquipmentId = "CR-101",
                CommandType = "SIM_INTERLOCK_AUDIT",
                RequiredPermission = "OPERATOR_SIM_TEST",
                SafeState = "ReadOnly",
                RequiredInterlocks = ["ILK-CR101-LUBE", "ILK-CV101-RUN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CR101-START",
                EquipmentId = "CR-101",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CR101-LUBE", "ILK-CV101-RUN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CR101-STOP",
                EquipmentId = "CR-101",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CR101-SPEED",
                EquipmentId = "CR-101",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 0.0,
                MaxParameter = 100.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CR101-LUBE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CR101-RESET",
                EquipmentId = "CR-101",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-SC102-START",
                EquipmentId = "SC-102",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-SC102-CHUTE", "ILK-CV102-RUN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-SC102-STOP",
                EquipmentId = "SC-102",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-SC102-RESET",
                EquipmentId = "SC-102",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV101-START",
                EquipmentId = "CV-101",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CV101-PULLCORD", "ILK-CV101-ALIGN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV101-STOP",
                EquipmentId = "CV-101",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV101-SPEED",
                EquipmentId = "CV-101",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 0.5,
                MaxParameter = 4.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CV101-PULLCORD"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV101-RESET",
                EquipmentId = "CV-101",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV102-START",
                EquipmentId = "CV-102",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CV102-PULLCORD", "ILK-CV102-TEMP"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV102-STOP",
                EquipmentId = "CV-102",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV102-SPEED",
                EquipmentId = "CV-102",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 0.5,
                MaxParameter = 4.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CV102-PULLCORD"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV102-RESET",
                EquipmentId = "CV-102",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },

            // Circuit 2: Thickener Circuit (TK-201, PP-201..203)
            new CauseAndEffectRule
            {
                RuleId = "CE-TK201-START",
                EquipmentId = "TK-201",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-TK201-TORQUE", "ILK-TK201-LEVEL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-TK201-STOP",
                EquipmentId = "TK-201",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-TK201-SPEED",
                EquipmentId = "TK-201",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 0.0,
                MaxParameter = 10.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-TK201-TORQUE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-TK201-RESET",
                EquipmentId = "TK-201",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP201-START",
                EquipmentId = "PP-201",
                CommandType = "START_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP201-SUCT", "ILK-PP201-SEAL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP201-STOP",
                EquipmentId = "PP-201",
                CommandType = "STOP_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP201-SPEED",
                EquipmentId = "PP-201",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_PUMPS",
                MinParameter = 100.0,
                MaxParameter = 550.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP201-SUCT"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP201-RESET",
                EquipmentId = "PP-201",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP202-START",
                EquipmentId = "PP-202",
                CommandType = "START_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP202-SUCT", "ILK-PP202-SEAL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP202-STOP",
                EquipmentId = "PP-202",
                CommandType = "STOP_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP202-SPEED",
                EquipmentId = "PP-202",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_PUMPS",
                MinParameter = 100.0,
                MaxParameter = 550.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP202-SUCT"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP202-RESET",
                EquipmentId = "PP-202",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP203-START",
                EquipmentId = "PP-203",
                CommandType = "START_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP203-FLOC-TANK"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP203-STOP",
                EquipmentId = "PP-203",
                CommandType = "STOP_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP203-SPEED",
                EquipmentId = "PP-203",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_PUMPS",
                MinParameter = 0.0,
                MaxParameter = 60.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP203-FLOC-TANK"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP203-RESET",
                EquipmentId = "PP-203",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },

            // Circuit 3: Magnetite Preparation (TK-301, MS-301, DM-301)
            new CauseAndEffectRule
            {
                RuleId = "CE-TK301-START",
                EquipmentId = "TK-301",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-TK301-MIN-LVL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-TK301-STOP",
                EquipmentId = "TK-301",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-TK301-DENSITY",
                EquipmentId = "TK-301",
                CommandType = "SET_DENSITY",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 1.20,
                MaxParameter = 2.00,
                SafeState = "NormalOperation",
                RequiredInterlocks = ["ILK-TK301-MIN-LVL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-TK301-RESET",
                EquipmentId = "TK-301",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-MS301-START",
                EquipmentId = "MS-301",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-MS301-DRUM-GUARD"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-MS301-STOP",
                EquipmentId = "MS-301",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-MS301-RESET",
                EquipmentId = "MS-301",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-DM301-START",
                EquipmentId = "DM-301",
                CommandType = "START_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-DM301-PRESSURE", "ILK-TK301-LEVEL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-DM301-STOP",
                EquipmentId = "DM-301",
                CommandType = "STOP_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-DM301-SPEED",
                EquipmentId = "DM-301",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_PUMPS",
                MinParameter = 200.0,
                MaxParameter = 1100.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-DM301-PRESSURE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-DM301-RESET",
                EquipmentId = "DM-301",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },

            // Circuit 4: Fine Coal & Froth Flotation (FC-401, AB-401, PP-401, PP-402)
            new CauseAndEffectRule
            {
                RuleId = "CE-FC401-START",
                EquipmentId = "FC-401",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-FC401-AIR-MIN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-FC401-STOP",
                EquipmentId = "FC-401",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-FC401-LEVEL",
                EquipmentId = "FC-401",
                CommandType = "SET_PULP_LEVEL",
                RequiredPermission = "CONTROL_VALVES",
                MinParameter = 30.0,
                MaxParameter = 95.0,
                SafeState = "Closed",
                RequiredInterlocks = ["ILK-FC401-TAIL-VALVE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-FC401-RESET",
                EquipmentId = "FC-401",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AB401-START",
                EquipmentId = "AB-401",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-AB401-OIL-TEMP"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AB401-STOP",
                EquipmentId = "AB-401",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AB401-SPEED",
                EquipmentId = "AB-401",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 200.0,
                MaxParameter = 2000.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-AB401-OIL-TEMP"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AB401-RESET",
                EquipmentId = "AB-401",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP401-START",
                EquipmentId = "PP-401",
                CommandType = "START_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP401-CHEMICAL-LVL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP401-STOP",
                EquipmentId = "PP-401",
                CommandType = "STOP_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP401-SPEED",
                EquipmentId = "PP-401",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_PUMPS",
                MinParameter = 0.0,
                MaxParameter = 35.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP401-CHEMICAL-LVL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP401-RESET",
                EquipmentId = "PP-401",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP402-START",
                EquipmentId = "PP-402",
                CommandType = "START_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP402-CHEMICAL-LVL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP402-STOP",
                EquipmentId = "PP-402",
                CommandType = "STOP_PUMP",
                RequiredPermission = "CONTROL_PUMPS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP402-SPEED",
                EquipmentId = "PP-402",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_PUMPS",
                MinParameter = 0.0,
                MaxParameter = 25.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-PP402-CHEMICAL-LVL"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-PP402-RESET",
                EquipmentId = "PP-402",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },

            // Circuit 5: Dewatering & Tailings (CF-501, FP-501, CV-501)
            new CauseAndEffectRule
            {
                RuleId = "CE-CF501-START",
                EquipmentId = "CF-501",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CF501-LUBE", "ILK-CF501-VIB-LOW", "ILK-CV501-RUN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CF501-STOP",
                EquipmentId = "CF-501",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CF501-SPEED",
                EquipmentId = "CF-501",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 500.0,
                MaxParameter = 1800.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CF501-LUBE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CF501-RESET",
                EquipmentId = "CF-501",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-FP501-START",
                EquipmentId = "FP-501",
                CommandType = "START_CYCLE",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "De-energized",
                RequiredInterlocks = ["ILK-FP501-LIGHT-CURTAIN", "ILK-FP501-DOOR-CLOSED"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-FP501-STOP",
                EquipmentId = "FP-501",
                CommandType = "STOP_CYCLE",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "De-energized",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-FP501-RESET",
                EquipmentId = "FP-501",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV501-START",
                EquipmentId = "CV-501",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-CV501-ALIGN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV501-STOP",
                EquipmentId = "CV-501",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-CV501-RESET",
                EquipmentId = "CV-501",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },

            // Circuit 6: ROM, Loadout & PGNAA (AN-601, SL-601, AF-001, LD-601)
            new CauseAndEffectRule
            {
                RuleId = "CE-AN601-CALIB",
                EquipmentId = "AN-601",
                CommandType = "CALIBRATE",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "ShutterClosed",
                RequiredInterlocks = ["ILK-AN601-SHUTTER"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AN601-INTERVAL",
                EquipmentId = "AN-601",
                CommandType = "SET_SAMPLE_INTERVAL",
                RequiredPermission = "CONTROL_RESET",
                MinParameter = 10.0,
                MaxParameter = 300.0,
                SafeState = "ShutterClosed",
                RequiredInterlocks = ["ILK-AN601-INTERLOCK-KEY"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AN601-RESET",
                EquipmentId = "AN-601",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "ShutterClosed",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-SL601-OPEN",
                EquipmentId = "SL-601",
                CommandType = "OPEN_GATE",
                RequiredPermission = "CONTROL_VALVES",
                SafeState = "Closed",
                RequiredInterlocks = ["ILK-SL601-LEVEL-MAX", "ILK-LD601-POSITION"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-SL601-CLOSE",
                EquipmentId = "SL-601",
                CommandType = "CLOSE_GATE",
                RequiredPermission = "CONTROL_VALVES",
                SafeState = "Closed",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-SL601-RESET",
                EquipmentId = "SL-601",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Closed",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AF001-START",
                EquipmentId = "AF-001",
                CommandType = "START_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-AF001-CHUTE", "ILK-CR101-RUN"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AF001-STOP",
                EquipmentId = "AF-001",
                CommandType = "STOP_MOTOR",
                RequiredPermission = "CONTROL_MOTORS",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AF001-SPEED",
                EquipmentId = "AF-001",
                CommandType = "SET_SPEED",
                RequiredPermission = "CONTROL_MOTORS",
                MinParameter = 0.0,
                MaxParameter = 100.0,
                SafeState = "Stopped",
                RequiredInterlocks = ["ILK-AF001-CHUTE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-AF001-RESET",
                EquipmentId = "AF-001",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "Stopped",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-LD601-START",
                EquipmentId = "LD-601",
                CommandType = "START_BATCH",
                RequiredPermission = "CONTROL_VALVES",
                SafeState = "ChuteClosed",
                RequiredInterlocks = ["ILK-LD601-WAGON-PRESENT", "ILK-LD601-BRAKE"],
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-LD601-ABORT",
                EquipmentId = "LD-601",
                CommandType = "ABORT_BATCH",
                RequiredPermission = "CONTROL_VALVES",
                SafeState = "ChuteClosed",
                IsApproved = true
            },
            new CauseAndEffectRule
            {
                RuleId = "CE-LD601-RESET",
                EquipmentId = "LD-601",
                CommandType = "RESET_FAULT",
                RequiredPermission = "CONTROL_RESET",
                SafeState = "ChuteClosed",
                IsApproved = true
            }
        ];
    }
}

internal sealed class CauseAndEffectDocument
{
    public string? Version { get; set; }
    public string? DocumentId { get; set; }
    public string? Facility { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedAt { get; set; }
    public string? Description { get; set; }
    public List<CauseAndEffectRule>? Rules { get; set; }
}
