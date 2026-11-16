using System.Diagnostics;

namespace ArchScada.ControlBoundary;

/// <summary>
/// Enforces the Arch SCADA Control Safety Boundary:
/// - Read-only / simulated by default unless real control is explicitly authorized and C&amp;E verified.
/// - Validates operator permissions, session, explicit confirmation, parameter range, interlocks,
///   communication quality, and Cause-and-Effect matrix registration.
/// - Strictly prohibits automatic shutdowns and automatic restarts.
/// - Audits every control attempt to an append-only JSONL log.
/// </summary>
public sealed class ControlBoundaryEngine
{
    private static readonly TimeSpan MaxTelemetryAgeForControl = TimeSpan.FromSeconds(5);

    private readonly ControlAuditLogger _auditLogger;
    private readonly SimulatedControlAdapter _simulatedAdapter;
    private readonly RealControlAdapter _realAdapter;
    private readonly ControlAuthorizationState _authorizationState;
    private readonly List<CauseAndEffectRule> _causeAndEffectRules = new();
    private readonly object _rulesLock = new();

    public ControlBoundaryEngine(
        ControlAuditLogger auditLogger,
        SimulatedControlAdapter? simulatedAdapter = null,
        RealControlAdapter? realAdapter = null,
        ControlAuthorizationState? authorizationState = null,
        IEnumerable<CauseAndEffectRule>? initialRules = null)
    {
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _authorizationState = authorizationState ?? new ControlAuthorizationState();
        _simulatedAdapter = simulatedAdapter ?? new SimulatedControlAdapter();
        _realAdapter = realAdapter ?? new RealControlAdapter(() => _authorizationState);

        if (initialRules is not null)
        {
            RegisterCauseAndEffectMatrix(initialRules, markVerified: _authorizationState.CauseAndEffectVerified);
        }
    }

    public ControlAuthorizationState AuthorizationState => _authorizationState;

    public void RegisterCauseAndEffectRule(CauseAndEffectRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_rulesLock)
        {
            _causeAndEffectRules.RemoveAll(r =>
                string.Equals(r.EquipmentId, rule.EquipmentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.CommandType, rule.CommandType, StringComparison.OrdinalIgnoreCase));
            _causeAndEffectRules.Add(rule);
        }
    }

    public void RegisterCauseAndEffectMatrix(IEnumerable<CauseAndEffectRule> rules, bool markVerified = true)
    {
        ArgumentNullException.ThrowIfNull(rules);

        lock (_rulesLock)
        {
            foreach (var rule in rules)
            {
                if (rule is null)
                {
                    continue;
                }

                _causeAndEffectRules.RemoveAll(r =>
                    string.Equals(r.EquipmentId, rule.EquipmentId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.CommandType, rule.CommandType, StringComparison.OrdinalIgnoreCase));
                _causeAndEffectRules.Add(rule);
            }
        }

        if (markVerified)
        {
            _authorizationState.CauseAndEffectVerified = true;
        }
    }

    public async Task<ControlCommandResult> ExecuteCommandAsync(
        ControlCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();

        // 1. Enforce prohibition on automatic shutdowns and automatic restarts.
        if (IsAutomaticShutdownOrRestart(request))
        {
            return await RejectAndAuditAsync(
                request,
                rule: null,
                ControlExecutionStatus.Rejected,
                "Automatic shutdowns and automatic restarts are prohibited; explicit manual operator command is required.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 2. Validate operator authentication and active session.
        if (string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return await RejectAndAuditAsync(
                request,
                rule: null,
                ControlExecutionStatus.Rejected,
                "Operator identity is required.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        if (!request.IsSessionActive)
        {
            return await RejectAndAuditAsync(
                request,
                rule: null,
                ControlExecutionStatus.Rejected,
                "Operator session is expired or inactive.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 3. Validate explicit operator confirmation flag.
        if (!request.OperatorConfirmed)
        {
            return await RejectAndAuditAsync(
                request,
                rule: null,
                ControlExecutionStatus.Rejected,
                "Explicit operator confirmation is required before executing control commands.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 4. Validate Cause-and-Effect matrix registration.
        var rule = FindApprovedRule(request.EquipmentId, request.CommandType);
        if (rule is null)
        {
            return await RejectAndAuditAsync(
                request,
                rule: null,
                ControlExecutionStatus.Blocked,
                $"No approved Cause-and-Effect rule registered for equipment '{request.EquipmentId}' and command '{request.CommandType}'.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 5. Validate operator permission (both request-level and C&E rule-level).
        if (!HasRequiredPermissions(request, rule, out var missingPermission))
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Rejected,
                $"Operator '{request.OperatorId}' lacks required permission '{missingPermission}'.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 6. Validate parameter range against Cause-and-Effect bounds.
        if (!IsParameterWithinRange(request, rule, out var rangeError))
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Rejected,
                rangeError!,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 7. Validate interlock status, equipment fault state, and communication quality.
        if (!request.InterlocksSatisfied)
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Blocked,
                "Safety or process interlocks are not satisfied.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.EquipmentFaulted &&
            !string.Equals(request.CommandType, "RESET_FAULT", StringComparison.OrdinalIgnoreCase))
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Blocked,
                $"Equipment '{request.EquipmentId}' is faulted and requires fault clearance before control.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        if (request.CommunicationQuality != CommunicationQuality.Good ||
            (request.LastTelemetryAge.HasValue && request.LastTelemetryAge.Value > MaxTelemetryAgeForControl))
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Blocked,
                "Communication quality is not Good; control commands are blocked.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        // 8. Select adapter: Simulated by default; Real only when explicitly authorized and C&E verified.
        if (!_authorizationState.IsRealControlAuthorized)
        {
            var simResult = await _simulatedAdapter.ExecuteAsync(request, rule, cancellationToken).ConfigureAwait(false);
            var audit = await WriteAuditAsync(
                request,
                rule,
                resultCode: "SIMULATED",
                feedback: simResult.EquipmentFeedbackState,
                simulated: true,
                reason: null,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);

            return simResult with { AuditId = audit.AuditId };
        }

        if (!_authorizationState.CauseAndEffectVerified)
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Blocked,
                "Real control is blocked: Cause-and-Effect matrix is not verified.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        if (_authorizationState.AuthorizedEquipmentIds.Count > 0 &&
            !_authorizationState.AuthorizedEquipmentIds.Any(id =>
                string.Equals(id, request.EquipmentId, StringComparison.OrdinalIgnoreCase)))
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Blocked,
                $"Equipment '{request.EquipmentId}' is outside the authorized control scope.",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var realResult = await _realAdapter.ExecuteAsync(request, rule, cancellationToken).ConfigureAwait(false);
            var audit = await WriteAuditAsync(
                request,
                rule,
                resultCode: MapStatusToAuditResult(realResult.Status),
                feedback: realResult.EquipmentFeedbackState,
                simulated: false,
                reason: realResult.FailureReason,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);

            return realResult with { AuditId = audit.AuditId };
        }
        catch (TimeoutException ex)
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Timeout,
                $"Command timed out without PLC confirmation: {ex.Message}",
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await RejectAndAuditAsync(
                request,
                rule,
                ControlExecutionStatus.Blocked,
                ex.Message,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private CauseAndEffectRule? FindApprovedRule(string equipmentId, string commandType)
    {
        lock (_rulesLock)
        {
            return _causeAndEffectRules.FirstOrDefault(r =>
                r.IsApproved &&
                string.Equals(r.EquipmentId, equipmentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.CommandType, commandType, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool IsAutomaticShutdownOrRestart(ControlCommandRequest request)
    {
        if (request.IsAutomaticTrigger)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.TriggerSource) &&
            request.TriggerSource.Contains("auto", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.CommandType.Contains("AUTO_SHUTDOWN", StringComparison.OrdinalIgnoreCase) ||
               request.CommandType.Contains("AUTOMATIC_SHUTDOWN", StringComparison.OrdinalIgnoreCase) ||
               request.CommandType.Contains("AUTO_RESTART", StringComparison.OrdinalIgnoreCase) ||
               request.CommandType.Contains("AUTOMATIC_RESTART", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRequiredPermissions(
        ControlCommandRequest request,
        CauseAndEffectRule rule,
        out string missingPermission)
    {
        if (!string.IsNullOrWhiteSpace(request.RequiredPermission) &&
            !request.OperatorPermissions.Any(p =>
                string.Equals(p, request.RequiredPermission, StringComparison.OrdinalIgnoreCase)))
        {
            missingPermission = request.RequiredPermission;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.RequiredPermission) &&
            !request.OperatorPermissions.Any(p =>
                string.Equals(p, rule.RequiredPermission, StringComparison.OrdinalIgnoreCase)))
        {
            missingPermission = rule.RequiredPermission;
            return false;
        }

        missingPermission = string.Empty;
        return true;
    }

    private static bool IsParameterWithinRange(
        ControlCommandRequest request,
        CauseAndEffectRule rule,
        out string? error)
    {
        if (!rule.MinParameter.HasValue && !rule.MaxParameter.HasValue)
        {
            error = null;
            return true;
        }

        if (!request.NumericParameter.HasValue)
        {
            error = $"Numeric parameter is required for command '{request.CommandType}' on '{request.EquipmentId}'.";
            return false;
        }

        var value = request.NumericParameter.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            error = "Numeric parameter must be a finite number.";
            return false;
        }

        if (rule.MinParameter.HasValue && value < rule.MinParameter.Value)
        {
            error = $"Parameter {value} is below minimum allowed {rule.MinParameter.Value}.";
            return false;
        }

        if (rule.MaxParameter.HasValue && value > rule.MaxParameter.Value)
        {
            error = $"Parameter {value} exceeds maximum allowed {rule.MaxParameter.Value}.";
            return false;
        }

        error = null;
        return true;
    }

    private async Task<ControlCommandResult> RejectAndAuditAsync(
        ControlCommandRequest request,
        CauseAndEffectRule? rule,
        ControlExecutionStatus status,
        string reason,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var audit = await WriteAuditAsync(
            request,
            rule,
            resultCode: MapStatusToAuditResult(status),
            feedback: null,
            simulated: !_authorizationState.IsRealControlAuthorized,
            reason: reason,
            duration,
            cancellationToken).ConfigureAwait(false);

        return new ControlCommandResult
        {
            Accepted = false,
            ExecutedOnDevice = false,
            IsSimulated = !_authorizationState.IsRealControlAuthorized,
            Status = status,
            Message = reason,
            FailureReason = reason,
            MatchedCauseAndEffectRuleId = rule?.RuleId,
            AuditId = audit.AuditId,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private Task<ControlAuditEntry> WriteAuditAsync(
        ControlCommandRequest request,
        CauseAndEffectRule? rule,
        string resultCode,
        string? feedback,
        bool simulated,
        string? reason,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var entry = new ControlAuditEntry
        {
            AuditId = Guid.NewGuid().ToString("D"),
            Timestamp = DateTimeOffset.UtcNow,
            User = request.OperatorId,
            Command = request.CommandType,
            Equipment = request.EquipmentId,
            Parameters = request.Parameters,
            Result = resultCode,
            Feedback = feedback,
            CeRule = rule?.RuleId,
            Interlocks = request.InterlockIds,
            CommunicationQuality = request.CommunicationQuality.ToString(),
            DurationSeconds = duration.TotalSeconds,
            Simulated = simulated,
            Reason = reason
        };

        return _auditLogger.AppendAsync(entry, cancellationToken);
    }

    private static string MapStatusToAuditResult(ControlExecutionStatus status) => status switch
    {
        ControlExecutionStatus.Simulated => "SIMULATED",
        ControlExecutionStatus.Success => "SUCCESS",
        ControlExecutionStatus.Timeout => "TIMEOUT",
        ControlExecutionStatus.Unconfirmed => "UNCONFIRMED",
        ControlExecutionStatus.Blocked => "BLOCKED",
        ControlExecutionStatus.Rejected => "REJECTED",
        _ => "UNCONFIRMED"
    };
}
