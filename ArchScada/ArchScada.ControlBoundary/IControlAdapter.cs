namespace ArchScada.ControlBoundary;

/// <summary>
/// Interface for executing or simulating validated control commands.
/// </summary>
public interface IControlAdapter
{
    string AdapterName { get; }
    bool IsSimulated { get; }

    Task<ControlCommandResult> ExecuteAsync(
        ControlCommandRequest request,
        CauseAndEffectRule rule,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatches a telecommand to the field PLC/RTU and verifies state transition via read-back feedback.
/// </summary>
public interface ITeleCommandDispatcher
{
    Task<(bool Sent, bool FeedbackConfirmed, string Detail)> DispatchAndVerifyAsync(
        ControlCommandRequest request,
        TimeSpan feedbackTimeout,
        CancellationToken ct);
}

