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
