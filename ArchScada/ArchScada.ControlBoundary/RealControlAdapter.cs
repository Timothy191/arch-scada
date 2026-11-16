namespace ArchScada.ControlBoundary;

/// <summary>
/// Real field-device control adapter. Disabled by default and strictly blocks/throws
/// unless IsRealControlAuthorized is explicitly enabled and CauseAndEffectVerified is true.
/// </summary>
public sealed class RealControlAdapter : IControlAdapter
{
    public static readonly TimeSpan DefaultFeedbackTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<ControlAuthorizationState> _authorizationProvider;
    private readonly Func<ControlCommandRequest, CauseAndEffectRule, CancellationToken, Task<string>>? _deviceWriter;
    private readonly ITeleCommandDispatcher? _teleCommandDispatcher;

    public RealControlAdapter(
        Func<ControlAuthorizationState>? authorizationProvider = null,
        Func<ControlCommandRequest, CauseAndEffectRule, CancellationToken, Task<string>>? deviceWriter = null,
        ITeleCommandDispatcher? teleCommandDispatcher = null,
        TimeSpan? feedbackTimeout = null)
    {
        _authorizationProvider = authorizationProvider ?? (() => new ControlAuthorizationState());
        _deviceWriter = deviceWriter;
        _teleCommandDispatcher = teleCommandDispatcher;
        FeedbackTimeout = feedbackTimeout is { TotalMilliseconds: > 0 }
            ? feedbackTimeout.Value
            : DefaultFeedbackTimeout;
    }

    public RealControlAdapter(
        Func<ControlAuthorizationState> authorizationProvider,
        ITeleCommandDispatcher teleCommandDispatcher,
        TimeSpan? feedbackTimeout = null)
        : this(authorizationProvider, deviceWriter: null, teleCommandDispatcher, feedbackTimeout)
    {
    }

    public RealControlAdapter(
        ITeleCommandDispatcher teleCommandDispatcher,
        ControlAuthorizationState? authorizationState = null,
        TimeSpan? feedbackTimeout = null)
        : this(
            authorizationState is not null ? () => authorizationState : null,
            deviceWriter: null,
            teleCommandDispatcher,
            feedbackTimeout)
    {
    }

    public string AdapterName => nameof(RealControlAdapter);
    public bool IsSimulated => false;
    public TimeSpan FeedbackTimeout { get; }

    public async Task<ControlCommandResult> ExecuteAsync(
        ControlCommandRequest request,
        CauseAndEffectRule rule,
        CancellationToken cancellationToken = default)
    {
        var auth = _authorizationProvider();
        if (!auth.IsRealControlAuthorized)
        {
            throw new InvalidOperationException(
                "Real control is disabled by default and is not authorized.");
        }

        if (!auth.CauseAndEffectVerified)
        {
            throw new InvalidOperationException(
                "Real control is blocked: Cause-and-Effect matrix has not been verified.");
        }

        if (_teleCommandDispatcher is not null)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FeedbackTimeout);

            try
            {
                var (sent, feedbackConfirmed, detail) = await _teleCommandDispatcher
                    .DispatchAndVerifyAsync(request, FeedbackTimeout, timeoutCts.Token)
                    .ConfigureAwait(false);

                if (sent && feedbackConfirmed)
                {
                    return new ControlCommandResult
                    {
                        Accepted = true,
                        ExecutedOnDevice = true,
                        IsSimulated = false,
                        Status = ControlExecutionStatus.Confirmed,
                        Message = string.IsNullOrWhiteSpace(detail)
                            ? $"Command {request.CommandType} confirmed on {request.EquipmentId}."
                            : detail,
                        EquipmentFeedbackState = detail,
                        MatchedCauseAndEffectRuleId = rule.RuleId,
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                }

                var unconfirmedReason = !sent
                    ? (string.IsNullOrWhiteSpace(detail) ? "Telecommand dispatch was not acknowledged by PLC." : detail)
                    : (string.IsNullOrWhiteSpace(detail) ? "PLC read-back feedback was not confirmed within timeout." : detail);

                return new ControlCommandResult
                {
                    Accepted = sent,
                    ExecutedOnDevice = sent,
                    IsSimulated = false,
                    Status = ControlExecutionStatus.Unconfirmed,
                    Message = unconfirmedReason,
                    FailureReason = unconfirmedReason,
                    EquipmentFeedbackState = detail,
                    MatchedCauseAndEffectRuleId = rule.RuleId,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var timeoutMessage = $"PLC read-back feedback timed out after {FeedbackTimeout.TotalSeconds:0.###}s for {request.CommandType} on {request.EquipmentId}.";
                return new ControlCommandResult
                {
                    Accepted = false,
                    ExecutedOnDevice = false,
                    IsSimulated = false,
                    Status = ControlExecutionStatus.Unconfirmed,
                    Message = timeoutMessage,
                    FailureReason = timeoutMessage,
                    MatchedCauseAndEffectRuleId = rule.RuleId,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }
            catch (TimeoutException ex)
            {
                var timeoutMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? $"PLC read-back feedback timed out after {FeedbackTimeout.TotalSeconds:0.###}s."
                    : ex.Message;

                return new ControlCommandResult
                {
                    Accepted = false,
                    ExecutedOnDevice = false,
                    IsSimulated = false,
                    Status = ControlExecutionStatus.Unconfirmed,
                    Message = timeoutMessage,
                    FailureReason = timeoutMessage,
                    MatchedCauseAndEffectRuleId = rule.RuleId,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }
        }

        if (_deviceWriter is null)
        {
            throw new InvalidOperationException(
                "Real control is authorized, but no field device driver is configured.");
        }

        var feedbackState = await _deviceWriter(request, rule, cancellationToken).ConfigureAwait(false);

        return new ControlCommandResult
        {
            Accepted = true,
            ExecutedOnDevice = true,
            IsSimulated = false,
            Status = ControlExecutionStatus.Success,
            Message = $"Command {request.CommandType} executed on {request.EquipmentId}.",
            EquipmentFeedbackState = feedbackState,
            MatchedCauseAndEffectRuleId = rule.RuleId,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }
}
