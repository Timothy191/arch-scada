using ArchScada.ControlBoundary;

namespace ArchScada.ControlBoundary.Tests;

public sealed class ControlBoundaryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _auditLogPath;

    public ControlBoundaryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"archscada-cb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _auditLogPath = Path.Combine(_tempDir, "control-audit.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static CauseAndEffectRule CreateMotorStartRule() => new()
    {
        RuleId = "Rule-101",
        EquipmentId = "M-101",
        CommandType = "START_MOTOR",
        RequiredPermission = "CONTROL_MOTORS",
        SafeState = "Stopped",
        RequiredInterlocks = new[] { "I-101" },
        IsApproved = true
    };

    private static CauseAndEffectRule CreateSpeedRule() => new()
    {
        RuleId = "Rule-102",
        EquipmentId = "M-101",
        CommandType = "SET_SPEED",
        RequiredPermission = "CONTROL_MOTORS",
        MinParameter = 10.0,
        MaxParameter = 100.0,
        SafeState = "Stopped",
        IsApproved = true
    };

    private static ControlCommandRequest CreateValidStartRequest() => new()
    {
        OperatorId = "operator1",
        OperatorPermissions = new[] { "CONTROL_MOTORS" },
        RequiredPermission = "CONTROL_MOTORS",
        IsSessionActive = true,
        CommandType = "START_MOTOR",
        EquipmentId = "M-101",
        OperatorConfirmed = true,
        InterlocksSatisfied = true,
        InterlockIds = new[] { "I-101" },
        CommunicationQuality = CommunicationQuality.Good
    };

    [Fact]
    public async Task DefaultMode_UsesSimulatedExecution_AndLogsSimulatedMarker()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var simulatedAdapter = new SimulatedControlAdapter();
        var realWriterCalled = false;
        var authState = new ControlAuthorizationState(); // Disabled by default
        var realAdapter = new RealControlAdapter(
            () => authState,
            (_, _, _) =>
            {
                realWriterCalled = true;
                return Task.FromResult("RUNNING");
            });

        var engine = new ControlBoundaryEngine(
            logger,
            simulatedAdapter,
            realAdapter,
            authState,
            new[] { CreateMotorStartRule() });

        var result = await engine.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.True(result.Accepted);
        Assert.False(result.ExecutedOnDevice);
        Assert.True(result.IsSimulated);
        Assert.Equal(ControlExecutionStatus.Simulated, result.Status);
        Assert.Contains("SIMULATED - NO DEVICE WRITE", result.Message);
        Assert.False(realWriterCalled);

        Assert.Single(simulatedAdapter.Logs);
        Assert.Contains("SIMULATED - NO DEVICE WRITE", simulatedAdapter.Logs[0]);

        var auditEntries = logger.ReadAll();
        Assert.Single(auditEntries);
        Assert.Equal("SIMULATED", auditEntries[0].Result);
        Assert.True(auditEntries[0].Simulated);
        Assert.Equal("Rule-101", auditEntries[0].CeRule);
    }

    [Fact]
    public async Task RealControlAdapter_Throws_WhenUnauthorizedByDefault()
    {
        var adapter = new RealControlAdapter();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ExecuteAsync(CreateValidStartRequest(), CreateMotorStartRule()));

        Assert.Contains("disabled by default", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealControlAdapter_Throws_WhenCauseAndEffectNotVerified()
    {
        var authState = new ControlAuthorizationState
        {
            IsRealControlAuthorized = true,
            CauseAndEffectVerified = false
        };
        var adapter = new RealControlAdapter(
            () => authState,
            (_, _, _) => Task.FromResult("RUNNING"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ExecuteAsync(CreateValidStartRequest(), CreateMotorStartRule()));

        Assert.Contains("Cause-and-Effect", ex.Message);
    }

    [Fact]
    public async Task RealControl_IsBlocked_WhenCauseAndEffectMatrixMissingOrUnverified()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var realWriterCalled = false;
        var authState = new ControlAuthorizationState
        {
            IsRealControlAuthorized = true,
            CauseAndEffectVerified = false
        };
        var realAdapter = new RealControlAdapter(
            () => authState,
            (_, _, _) =>
            {
                realWriterCalled = true;
                return Task.FromResult("RUNNING");
            });

        // Case 1: No C&E rule registered at all
        var engineNoRules = new ControlBoundaryEngine(logger, realAdapter: realAdapter, authorizationState: authState);
        var missingRuleResult = await engineNoRules.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.False(missingRuleResult.Accepted);
        Assert.False(missingRuleResult.ExecutedOnDevice);
        Assert.Equal(ControlExecutionStatus.Blocked, missingRuleResult.Status);
        Assert.False(realWriterCalled);

        // Case 2: C&E rule registered, but CauseAndEffectVerified is still false
        engineNoRules.RegisterCauseAndEffectRule(CreateMotorStartRule());
        var unverifiedResult = await engineNoRules.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.False(unverifiedResult.Accepted);
        Assert.False(unverifiedResult.ExecutedOnDevice);
        Assert.Equal(ControlExecutionStatus.Blocked, unverifiedResult.Status);
        Assert.False(realWriterCalled);

        var audits = logger.ReadAll();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("BLOCKED", a.Result));
    }

    [Fact]
    public async Task RealControl_Succeeds_WhenAuthorizedAndCauseAndEffectVerified()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var authState = new ControlAuthorizationState
        {
            IsRealControlAuthorized = true,
            CauseAndEffectVerified = true,
            AuthorizedBy = "PlantManager"
        };
        var realAdapter = new RealControlAdapter(
            () => authState,
            (_, _, _) => Task.FromResult("RUNNING"));

        var engine = new ControlBoundaryEngine(
            logger,
            realAdapter: realAdapter,
            authorizationState: authState,
            initialRules: new[] { CreateMotorStartRule() });

        var result = await engine.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.True(result.Accepted);
        Assert.True(result.ExecutedOnDevice);
        Assert.False(result.IsSimulated);
        Assert.Equal(ControlExecutionStatus.Success, result.Status);
        Assert.Equal("RUNNING", result.EquipmentFeedbackState);

        var audits = logger.ReadAll();
        Assert.Single(audits);
        Assert.Equal("SUCCESS", audits[0].Result);
        Assert.False(audits[0].Simulated);
    }

    [Fact]
    public async Task UnconfirmedCommand_IsRejectedAndAudited()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var engine = new ControlBoundaryEngine(
            logger,
            initialRules: new[] { CreateMotorStartRule() });

        var unconfirmed = CreateValidStartRequest() with { OperatorConfirmed = false };
        var result = await engine.ExecuteCommandAsync(unconfirmed);

        Assert.False(result.Accepted);
        Assert.False(result.ExecutedOnDevice);
        Assert.Equal(ControlExecutionStatus.Rejected, result.Status);
        Assert.Contains("confirmation", result.FailureReason, StringComparison.OrdinalIgnoreCase);

        var audits = logger.ReadAll();
        Assert.Single(audits);
        Assert.Equal("REJECTED", audits[0].Result);
        Assert.False(string.IsNullOrWhiteSpace(audits[0].Hash));
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(105.0)]
    public async Task OutOfRangeParameter_IsRejectedAndAudited(double speedValue)
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var engine = new ControlBoundaryEngine(
            logger,
            initialRules: new[] { CreateSpeedRule() });

        var request = CreateValidStartRequest() with
        {
            CommandType = "SET_SPEED",
            NumericParameter = speedValue,
            Parameters = new Dictionary<string, object?> { ["speed"] = speedValue }
        };

        var result = await engine.ExecuteCommandAsync(request);

        Assert.False(result.Accepted);
        Assert.False(result.ExecutedOnDevice);
        Assert.Equal(ControlExecutionStatus.Rejected, result.Status);
        Assert.NotNull(result.FailureReason);

        var audits = logger.ReadAll();
        Assert.Single(audits);
        Assert.Equal("REJECTED", audits[0].Result);
    }

    [Fact]
    public async Task AutomaticShutdownAndRestart_AreStrictlyRejectedAndAudited()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var engine = new ControlBoundaryEngine(
            logger,
            initialRules: new[] { CreateMotorStartRule() });

        var autoTrigger = CreateValidStartRequest() with { IsAutomaticTrigger = true };
        var autoRestart = CreateValidStartRequest() with { CommandType = "AUTO_RESTART" };

        var r1 = await engine.ExecuteCommandAsync(autoTrigger);
        var r2 = await engine.ExecuteCommandAsync(autoRestart);

        Assert.False(r1.Accepted);
        Assert.Equal(ControlExecutionStatus.Rejected, r1.Status);
        Assert.False(r2.Accepted);
        Assert.Equal(ControlExecutionStatus.Rejected, r2.Status);

        var audits = logger.ReadAll();
        Assert.Equal(2, audits.Count);
        Assert.Equal("GENESIS", audits[0].PreviousHash);
        Assert.Equal(audits[0].Hash, audits[1].PreviousHash);
    }

    [Fact]
    public async Task MissingPermission_And_UnsatisfiedInterlock_AreRejectedOrBlockedAndAudited()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var engine = new ControlBoundaryEngine(
            logger,
            initialRules: new[] { CreateMotorStartRule() });

        var noPerm = CreateValidStartRequest() with { OperatorPermissions = Array.Empty<string>() };
        var noInterlock = CreateValidStartRequest() with { InterlocksSatisfied = false };

        var permResult = await engine.ExecuteCommandAsync(noPerm);
        var interlockResult = await engine.ExecuteCommandAsync(noInterlock);

        Assert.False(permResult.Accepted);
        Assert.Equal(ControlExecutionStatus.Rejected, permResult.Status);

        Assert.False(interlockResult.Accepted);
        Assert.Equal(ControlExecutionStatus.Blocked, interlockResult.Status);

        var audits = logger.ReadAll();
        Assert.Equal(2, audits.Count);
    }

    [Fact]
    public async Task RealControlAdapter_WithTeleCommandDispatcher_ReturnsConfirmed_WhenFeedbackConfirmed()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var authState = new ControlAuthorizationState
        {
            IsRealControlAuthorized = true,
            CauseAndEffectVerified = true,
            AuthorizedBy = "PlantManager"
        };

        var dispatcher = new StubTeleCommandDispatcher((req, timeout, ct) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(3), timeout);
            return Task.FromResult((Sent: true, FeedbackConfirmed: true, Detail: "RUNNING"));
        });

        var realAdapter = new RealControlAdapter(() => authState, dispatcher);
        var engine = new ControlBoundaryEngine(
            logger,
            realAdapter: realAdapter,
            authorizationState: authState,
            initialRules: new[] { CreateMotorStartRule() });

        var result = await engine.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.True(result.Accepted);
        Assert.True(result.ExecutedOnDevice);
        Assert.False(result.IsSimulated);
        Assert.Equal(ControlExecutionStatus.Confirmed, result.Status);
        Assert.Equal("RUNNING", result.EquipmentFeedbackState);
        Assert.Equal(1, dispatcher.CallCount);

        var audits = logger.ReadAll();
        Assert.Single(audits);
        Assert.Equal("CONFIRMED", audits[0].Result);
        Assert.Equal("RUNNING", audits[0].Feedback);
        Assert.False(audits[0].Simulated);
    }

    [Fact]
    public async Task RealControlAdapter_WithTeleCommandDispatcher_ReturnsUnconfirmed_WhenFeedbackTimesOutOrUnconfirmed()
    {
        var logger = new ControlAuditLogger(_auditLogPath);
        var authState = new ControlAuthorizationState
        {
            IsRealControlAuthorized = true,
            CauseAndEffectVerified = true,
            AuthorizedBy = "PlantManager"
        };

        // Case 1: Dispatcher reports sent=true, feedbackConfirmed=false (unconfirmed within timeout)
        var unconfirmedDispatcher = new StubTeleCommandDispatcher((_, _, _) =>
            Task.FromResult((Sent: true, FeedbackConfirmed: false, Detail: "PLC read-back remained STOPPED after 3s")));

        var realAdapter1 = new RealControlAdapter(() => authState, unconfirmedDispatcher);
        var engine1 = new ControlBoundaryEngine(
            logger,
            realAdapter: realAdapter1,
            authorizationState: authState,
            initialRules: new[] { CreateMotorStartRule() });

        var result1 = await engine1.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.Equal(ControlExecutionStatus.Unconfirmed, result1.Status);
        Assert.NotEqual(ControlExecutionStatus.Confirmed, result1.Status);
        Assert.False(result1.IsSimulated);
        Assert.Contains("STOPPED", result1.FailureReason);

        // Case 2: Dispatcher exceeds FeedbackTimeout and gets cancelled by the adapter timeout token
        var delayedDispatcher = new StubTeleCommandDispatcher(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return (Sent: true, FeedbackConfirmed: true, Detail: "RUNNING");
        });

        var realAdapter2 = new RealControlAdapter(
            () => authState,
            delayedDispatcher,
            feedbackTimeout: TimeSpan.FromMilliseconds(50));
        var engine2 = new ControlBoundaryEngine(
            logger,
            realAdapter: realAdapter2,
            authorizationState: authState,
            initialRules: new[] { CreateMotorStartRule() });

        var result2 = await engine2.ExecuteCommandAsync(CreateValidStartRequest());

        Assert.Equal(ControlExecutionStatus.Unconfirmed, result2.Status);
        Assert.NotEqual(ControlExecutionStatus.Confirmed, result2.Status);
        Assert.False(result2.IsSimulated);
        Assert.Contains("timed out", result2.FailureReason, StringComparison.OrdinalIgnoreCase);

        var audits = logger.ReadAll();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("UNCONFIRMED", a.Result));
    }

    private sealed class StubTeleCommandDispatcher : ITeleCommandDispatcher
    {
        private readonly Func<ControlCommandRequest, TimeSpan, CancellationToken, Task<(bool Sent, bool FeedbackConfirmed, string Detail)>> _handler;

        public StubTeleCommandDispatcher(
            Func<ControlCommandRequest, TimeSpan, CancellationToken, Task<(bool Sent, bool FeedbackConfirmed, string Detail)>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<(bool Sent, bool FeedbackConfirmed, string Detail)> DispatchAndVerifyAsync(
            ControlCommandRequest request,
            TimeSpan feedbackTimeout,
            CancellationToken ct)
        {
            CallCount++;
            return _handler(request, feedbackTimeout, ct);
        }
    }
}

