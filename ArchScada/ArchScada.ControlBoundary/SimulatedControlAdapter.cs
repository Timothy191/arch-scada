namespace ArchScada.ControlBoundary;

/// <summary>
/// Default control adapter during development and read-only commissioning.
/// Never writes to any PLC or field device; always logs "SIMULATED - NO DEVICE WRITE".
/// </summary>
public sealed class SimulatedControlAdapter : IControlAdapter
{
    public const string SimulatedLogMarker = "SIMULATED - NO DEVICE WRITE";

    private readonly Action<string>? _logSink;
    private readonly List<string> _logs = new();
    private readonly object _logLock = new();

    public SimulatedControlAdapter(Action<string>? logSink = null)
    {
        _logSink = logSink;
    }

    public string AdapterName => nameof(SimulatedControlAdapter);
    public bool IsSimulated => true;

    public IReadOnlyList<string> Logs
    {
        get
        {
            lock (_logLock)
            {
                return _logs.ToArray();
            }
        }
    }

    public Task<ControlCommandResult> ExecuteAsync(
        ControlCommandRequest request,
        CauseAndEffectRule rule,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logLine = $"{SimulatedLogMarker} | Command={request.CommandType} | Equipment={request.EquipmentId} | User={request.OperatorId} | Rule={rule.RuleId}";

        lock (_logLock)
        {
            _logs.Add(logLine);
        }

        _logSink?.Invoke(logLine);

        var result = new ControlCommandResult
        {
            Accepted = true,
            ExecutedOnDevice = false,
            IsSimulated = true,
            Status = ControlExecutionStatus.Simulated,
            Message = SimulatedLogMarker,
            EquipmentFeedbackState = "SIMULATED",
            MatchedCauseAndEffectRuleId = rule.RuleId,
            CompletedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(result);
    }
}
