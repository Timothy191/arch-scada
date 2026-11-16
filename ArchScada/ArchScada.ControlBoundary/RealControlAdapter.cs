namespace ArchScada.ControlBoundary;

/// <summary>
/// Real field-device control adapter. Disabled by default and strictly blocks/throws
/// unless IsRealControlAuthorized is explicitly enabled and CauseAndEffectVerified is true.
/// </summary>
public sealed class RealControlAdapter : IControlAdapter
{
    private readonly Func<ControlAuthorizationState> _authorizationProvider;
    private readonly Func<ControlCommandRequest, CauseAndEffectRule, CancellationToken, Task<string>>? _deviceWriter;

    public RealControlAdapter(
        Func<ControlAuthorizationState>? authorizationProvider = null,
        Func<ControlCommandRequest, CauseAndEffectRule, CancellationToken, Task<string>>? deviceWriter = null)
    {
        _authorizationProvider = authorizationProvider ?? (() => new ControlAuthorizationState());
        _deviceWriter = deviceWriter;
    }

    public string AdapterName => nameof(RealControlAdapter);
    public bool IsSimulated => false;

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
