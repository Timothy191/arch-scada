using System;
using System.IO;
using System.Threading.Tasks;
using ArchScada.ControlBoundary;
using ArchScada.CoreIntegration;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;
using Xunit;

namespace ArchScada.Integration.Tests;

public class ControlBoundaryToDesktopIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _auditLogPath;

    public ControlBoundaryToDesktopIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archscada-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _auditLogPath = Path.Combine(_tempDir, "integration-audit.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EndToEnd_SimulatedControlFlow_FromDesktopThroughBoundaryToAudit()
    {
        // Arrange: Build the full chain Desktop → ControlBoundary → Audit
        var auditLogger = new ControlAuditLogger(_auditLogPath);
        var simulatedAdapter = new SimulatedControlAdapter();
        var authState = new ControlAuthorizationState(); // Real control disabled by default
        var realAdapter = new RealControlAdapter(
            () => authState,
            (_, _, _) => Task.FromResult("RUNNING"));

        var engine = new ControlBoundaryEngine(
            auditLogger: auditLogger,
            simulatedAdapter: simulatedAdapter,
            realAdapter: realAdapter,
            authorizationState: authState,
            initialRules: new[]
            {
                new CauseAndEffectRule
                {
                    RuleId = "CE-CR101-START",
                    EquipmentId = "CR-101",
                    CommandType = "START_MOTOR",
                    RequiredPermission = "CONTROL_MOTORS",
                    SafeState = "Stopped",
                    RequiredInterlocks = new[] { "ILK-CR101-LUBE", "ILK-CV101-RUN" },
                    IsApproved = true
                }
            });

        // Act: Execute a command through the boundary
        var request = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = new[] { "CONTROL_MOTORS" },
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "START_MOTOR",
            EquipmentId = "CR-101",
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            InterlockIds = new[] { "ILK-CR101-LUBE", "ILK-CV101-RUN" },
            CommunicationQuality = CommunicationQuality.Good
        };

        var result = await engine.ExecuteCommandAsync(request);

        // Assert: Command is accepted but simulated (real control not authorized)
        Assert.True(result.Accepted);
        Assert.False(result.ExecutedOnDevice);
        Assert.True(result.IsSimulated);
        Assert.Equal(ControlExecutionStatus.Simulated, result.Status);
        Assert.Equal("CE-CR101-START", result.MatchedCauseAndEffectRuleId);

        // Assert: Audit trail contains the simulated execution
        var audits = auditLogger.ReadAll();
        Assert.Single(audits);
        Assert.True(audits[0].Simulated);
        Assert.Equal("CR-101", audits[0].Equipment);
        Assert.Equal("START_MOTOR", audits[0].Command);
        Assert.Equal("operator1", audits[0].User);
    }

    [Fact]
    public async Task EndToEnd_RealControlBlocked_WhenNotAuthorized()
    {
        // Arrange
        var auditLogger = new ControlAuditLogger(_auditLogPath);
        var engine = new ControlBoundaryEngine(
            auditLogger,
            authorizationState: new ControlAuthorizationState(), // Not authorized
            initialRules: new[]
            {
                new CauseAndEffectRule
                {
                    RuleId = "CE-M101-START",
                    EquipmentId = "M-101",
                    CommandType = "START_MOTOR",
                    RequiredPermission = "CONTROL_MOTORS",
                    SafeState = "Stopped",
                    IsApproved = true
                }
            });

        // Act
        var request = new ControlCommandRequest
        {
            OperatorId = "operator1",
            OperatorPermissions = new[] { "CONTROL_MOTORS" },
            RequiredPermission = "CONTROL_MOTORS",
            IsSessionActive = true,
            CommandType = "START_MOTOR",
            EquipmentId = "M-101",
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            CommunicationQuality = CommunicationQuality.Good
        };

        var result = await engine.ExecuteCommandAsync(request);

        // Assert: Real control is blocked because not authorized.
        // Command goes through simulated adapter (Accepted=true, IsSimulated=true).
        Assert.True(result.Accepted);
        Assert.True(result.IsSimulated);
        Assert.Equal(ControlExecutionStatus.Simulated, result.Status);
    }

    [Fact]
    public void IControlBoundaryEngine_Interface_IsImplementedByControlBoundaryEngine()
    {
        // Arrange & Act
        var engine = new ControlBoundaryEngine(
            new ControlAuditLogger(Path.Combine(_tempDir, "interface-test.jsonl")),
            authorizationState: new ControlAuthorizationState());

        // Assert: The concrete class implements the interface
        IControlBoundaryEngine engineAsInterface = engine;
        Assert.NotNull(engineAsInterface);
        Assert.Same(engine.AuthorizationState, engineAsInterface.AuthorizationState);
    }
}
