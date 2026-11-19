using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArchScada.ControlBoundary;

/// <summary>
/// Interface for the Control Safety Boundary engine.
/// Enforces simulated-by-default control with Cause-and-Effect matrix validation,
/// operator authorization, interlock checks, and append-only audit logging.
/// </summary>
public interface IControlBoundaryEngine
{
    /// <summary>
    /// Gets the current control authorization state.
    /// </summary>
    ControlAuthorizationState AuthorizationState { get; }

    /// <summary>
    /// Registers a single Cause-and-Effect rule.
    /// </summary>
    void RegisterCauseAndEffectRule(CauseAndEffectRule rule);

    /// <summary>
    /// Registers a full Cause-and-Effect matrix from a collection of rules.
    /// </summary>
    void RegisterCauseAndEffectMatrix(IEnumerable<CauseAndEffectRule> rules, bool markVerified = true);

    /// <summary>
    /// Executes a validated control command through the safety boundary.
    /// </summary>
    Task<ControlCommandResult> ExecuteCommandAsync(
        ControlCommandRequest request,
        CancellationToken cancellationToken = default);
}