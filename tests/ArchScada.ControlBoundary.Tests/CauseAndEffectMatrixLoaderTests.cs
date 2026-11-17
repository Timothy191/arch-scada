using System.Text.Json;
using Xunit;

namespace ArchScada.ControlBoundary.Tests;

public sealed class CauseAndEffectMatrixLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _auditLogPath;

    public CauseAndEffectMatrixLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archscada-ce-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _auditLogPath = Path.Combine(_tempDir, "ce-audit.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static string ResolveRepoPath(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine([current, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current)
            {
                break;
            }

            current = parent.FullName;
        }

        var fallback = Path.GetFullPath(Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]));
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException($"Could not locate file: {Path.Combine(relativeParts)}");
    }

    // ---------------------------------------------------------------------------
    // 1. Matrix File Loading and 6 CHPP Circuits Coverage
    // ---------------------------------------------------------------------------

    [Fact]
    public void CauseAndEffectMatrix_LoadsFromDisk_AndValidatesAllRules()
    {
        var matrixPath = ResolveRepoPath("ArchScada", "Config", "CauseAndEffectMatrix.json");
        Assert.True(File.Exists(matrixPath), $"Matrix JSON not found at {matrixPath}");

        var rules = CauseAndEffectMatrixLoader.LoadMatrix(matrixPath);
        Assert.NotEmpty(rules);

        foreach (var rule in rules)
        {
            Assert.True(CauseAndEffectMatrixLoader.ValidateRule(rule, out var error), error);
            Assert.False(string.IsNullOrWhiteSpace(rule.RuleId));
            Assert.False(string.IsNullOrWhiteSpace(rule.EquipmentId));
            Assert.False(string.IsNullOrWhiteSpace(rule.CommandType));
            Assert.False(string.IsNullOrWhiteSpace(rule.SafeState));
            Assert.True(rule.IsApproved);
        }
    }

    [Fact]
    public void CauseAndEffectMatrix_CoversAllSixChppCircuits()
    {
        var matrixPath = ResolveRepoPath("ArchScada", "Config", "CauseAndEffectMatrix.json");
        var rules = CauseAndEffectMatrixLoader.LoadMatrix(matrixPath);

        // Required equipment across all 6 CHPP circuits from brief
        string[] circuit1 = ["CR-101", "SC-102", "CV-101", "CV-102"];
        string[] circuit2 = ["TK-201", "PP-201", "PP-202", "PP-203"];
        string[] circuit3 = ["TK-301", "MS-301", "DM-301"];
        string[] circuit4 = ["FC-401", "AB-401", "PP-401", "PP-402"];
        string[] circuit5 = ["CF-501", "FP-501", "CV-501"];
        string[] circuit6 = ["AN-601", "SL-601", "AF-001", "LD-601"];

        var allRequired = circuit1
            .Concat(circuit2)
            .Concat(circuit3)
            .Concat(circuit4)
            .Concat(circuit5)
            .Concat(circuit6)
            .Distinct();

        var equipmentIds = rules.Select(r => r.EquipmentId).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet();

        foreach (var eqId in allRequired)
        {
            Assert.Contains(eqId, equipmentIds);
        }

        // Verify specific circuit coverage
        Assert.All(circuit1, eq => Assert.Contains(eq, equipmentIds));
        Assert.All(circuit2, eq => Assert.Contains(eq, equipmentIds));
        Assert.All(circuit3, eq => Assert.Contains(eq, equipmentIds));
        Assert.All(circuit4, eq => Assert.Contains(eq, equipmentIds));
        Assert.All(circuit5, eq => Assert.Contains(eq, equipmentIds));
        Assert.All(circuit6, eq => Assert.Contains(eq, equipmentIds));
    }

    // ---------------------------------------------------------------------------
    // 2. Built-in Fallback Defaults and Path Resolution
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetDefaultMatrix_ReturnsComprehensiveRulesAcrossAllSixCircuits()
    {
        var defaults = CauseAndEffectMatrixLoader.GetDefaultMatrix();
        Assert.NotEmpty(defaults);

        var equipmentIds = defaults.Select(r => r.EquipmentId).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet();

        Assert.Contains("CR-101", equipmentIds);
        Assert.Contains("SC-102", equipmentIds);
        Assert.Contains("CV-101", equipmentIds);
        Assert.Contains("CV-102", equipmentIds);
        Assert.Contains("TK-201", equipmentIds);
        Assert.Contains("PP-201", equipmentIds);
        Assert.Contains("PP-202", equipmentIds);
        Assert.Contains("PP-203", equipmentIds);
        Assert.Contains("TK-301", equipmentIds);
        Assert.Contains("FC-401", equipmentIds);
        Assert.Contains("CF-501", equipmentIds);
        Assert.Contains("AN-601", equipmentIds);
        Assert.Contains("SL-601", equipmentIds);
    }

    [Fact]
    public void LoadMatrix_WithInvalidOrMissingPath_FallsBackToDefaultMatrix()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist.json");
        var rules = CauseAndEffectMatrixLoader.LoadMatrix(nonExistent);

        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => r.EquipmentId == "CR-101");
        Assert.Contains(rules, r => r.EquipmentId == "CF-501");
    }

    // ---------------------------------------------------------------------------
    // 3. JSON Parsing and Rule Validation
    // ---------------------------------------------------------------------------

    [Fact]
    public void LoadFromJson_SupportsBothDocumentAndArrayFormats()
    {
        var rule1 = new CauseAndEffectRule
        {
            RuleId = "TEST-01",
            EquipmentId = "CR-101",
            CommandType = "START_MOTOR",
            RequiredPermission = "CONTROL_MOTORS",
            SafeState = "Stopped",
            RequiredInterlocks = ["ILK-1"],
            IsApproved = true
        };

        // Format A: Object with "rules" property
        var docJson = JsonSerializer.Serialize(new { rules = new[] { rule1 } });
        var loadedDoc = CauseAndEffectMatrixLoader.LoadFromJson(docJson);
        Assert.Single(loadedDoc);
        Assert.Equal("TEST-01", loadedDoc[0].RuleId);

        // Format B: Direct array of rules
        var arrayJson = JsonSerializer.Serialize(new[] { rule1 });
        var loadedArray = CauseAndEffectMatrixLoader.LoadFromJson(arrayJson);
        Assert.Single(loadedArray);
        Assert.Equal("TEST-01", loadedArray[0].RuleId);
    }

    [Fact]
    public void ValidateRule_IdentifiesInvalidBoundsAndMissingIdentifiers()
    {
        // Null rule
        Assert.False(CauseAndEffectMatrixLoader.ValidateRule(null, out var errNull));
        Assert.Contains("null", errNull, StringComparison.OrdinalIgnoreCase);

        // Empty RuleId
        var ruleNoId = new CauseAndEffectRule { RuleId = "", EquipmentId = "CR-101", CommandType = "START" };
        Assert.False(CauseAndEffectMatrixLoader.ValidateRule(ruleNoId, out var errId));
        Assert.Contains("RuleId", errId, StringComparison.OrdinalIgnoreCase);

        // Empty EquipmentId
        var ruleNoEq = new CauseAndEffectRule { RuleId = "R1", EquipmentId = "", CommandType = "START" };
        Assert.False(CauseAndEffectMatrixLoader.ValidateRule(ruleNoEq, out var errEq));
        Assert.Contains("EquipmentId", errEq, StringComparison.OrdinalIgnoreCase);

        // Inverted parameter bounds (Min > Max)
        var ruleInvertedBounds = new CauseAndEffectRule
        {
            RuleId = "R1",
            EquipmentId = "CR-101",
            CommandType = "SET_SPEED",
            MinParameter = 100.0,
            MaxParameter = 10.0
        };
        Assert.False(CauseAndEffectMatrixLoader.ValidateRule(ruleInvertedBounds, out var errBounds));
        Assert.Contains("MinParameter", errBounds, StringComparison.OrdinalIgnoreCase);

        // Valid rule
        var validRule = new CauseAndEffectRule
        {
            RuleId = "R1",
            EquipmentId = "CR-101",
            CommandType = "SET_SPEED",
            MinParameter = 10.0,
            MaxParameter = 100.0,
            SafeState = "Stopped"
        };
        Assert.True(CauseAndEffectMatrixLoader.ValidateRule(validRule, out var errValid));
        Assert.Null(errValid);
    }

    // ---------------------------------------------------------------------------
    // 4. ControlBoundaryEngine Enforcement with Loaded Matrix
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ControlBoundaryEngine_EnforcesRulesFromLoadedMatrix()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var rules = CauseAndEffectMatrixLoader.LoadMatrix();

        var engine = new ControlBoundaryEngine(
            logger,
            authorizationState: new ControlAuthorizationState
            {
                IsRealControlAuthorized = false,
                CauseAndEffectVerified = true
            },
            initialRules: rules);

        // 1. Valid command on Circuit 1 (CR-101) with satisfied interlocks
        var validCr101Request = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = ["CONTROL_MOTORS"],
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "START_MOTOR",
            EquipmentId = "CR-101",
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            InterlockIds = ["ILK-CR101-LUBE", "ILK-CV101-RUN"],
            CommunicationQuality = CommunicationQuality.Good
        };

        var cr101Result = await engine.ExecuteCommandAsync(validCr101Request);
        Assert.True(cr101Result.Accepted);
        Assert.Equal(ControlExecutionStatus.Simulated, cr101Result.Status);
        Assert.True(cr101Result.IsSimulated);
        Assert.Equal("CE-CR101-START", cr101Result.MatchedCauseAndEffectRuleId);

        // 2. Valid command on Circuit 5 (CF-501 centrifuge)
        var validCf501Request = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = ["CONTROL_MOTORS"],
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "START_MOTOR",
            EquipmentId = "CF-501",
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            InterlockIds = ["ILK-CF501-LUBE", "ILK-CF501-VIB-LOW", "ILK-CV501-RUN"],
            CommunicationQuality = CommunicationQuality.Good
        };

        var cf501Result = await engine.ExecuteCommandAsync(validCf501Request);
        Assert.True(cf501Result.Accepted);
        Assert.Equal(ControlExecutionStatus.Simulated, cf501Result.Status);
        Assert.Equal("CE-CF501-START", cf501Result.MatchedCauseAndEffectRuleId);

        // 3. Command on unregistered equipment -> Blocked
        var unknownEquipmentRequest = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = ["CONTROL_MOTORS"],
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "START_MOTOR",
            EquipmentId = "UNKNOWN-999",
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            CommunicationQuality = CommunicationQuality.Good
        };

        var unknownResult = await engine.ExecuteCommandAsync(unknownEquipmentRequest);
        Assert.False(unknownResult.Accepted);
        Assert.Equal(ControlExecutionStatus.Blocked, unknownResult.Status);
        Assert.Contains("No approved Cause-and-Effect rule", unknownResult.FailureReason);

        // 4. Parameter out of bounds on CV-101 (min: 0.5, max: 4.0, requested: 9.5) -> Rejected
        var outOfBoundsRequest = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = ["CONTROL_MOTORS"],
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "SET_SPEED",
            EquipmentId = "CV-101",
            NumericParameter = 9.5,
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            CommunicationQuality = CommunicationQuality.Good
        };

        var outOfBoundsResult = await engine.ExecuteCommandAsync(outOfBoundsRequest);
        Assert.False(outOfBoundsResult.Accepted);
        Assert.Equal(ControlExecutionStatus.Rejected, outOfBoundsResult.Status);
        Assert.Contains("exceeds maximum allowed", outOfBoundsResult.FailureReason);

        // 5. Unsatisfied interlocks -> Blocked
        var interlockFailedRequest = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = ["CONTROL_MOTORS"],
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "START_MOTOR",
            EquipmentId = "CR-101",
            OperatorConfirmed = true,
            InterlocksSatisfied = false, // Interlocks tripped
            CommunicationQuality = CommunicationQuality.Good
        };

        var interlockResult = await engine.ExecuteCommandAsync(interlockFailedRequest);
        Assert.False(interlockResult.Accepted);
        Assert.Equal(ControlExecutionStatus.Blocked, interlockResult.Status);
        Assert.Contains("interlocks are not satisfied", interlockResult.FailureReason);
    }
}
