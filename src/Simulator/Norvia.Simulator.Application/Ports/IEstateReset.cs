using Norvia.Simulator.Application.Reset;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The reset door each application owns (ADR 0039): the TMS API, the Partner portal and the Driver app API each delete what a run or a person created
/// and keep their reference data. An application that can't be reached is an answer, not an error: <see cref="AppReadiness.Reachable"/> is false, or the
/// result is a refusal.
/// </summary>
public interface IEstateReset
{
    /// <summary>Asks whether the application would take a reset now.</summary>
    Task<AppReadiness> CheckAsync(EstateApp app, CancellationToken cancellationToken);

    /// <summary>Tells the application to reset, and returns what it deleted or why it refused.</summary>
    Task<AppResetResult> ResetAsync(EstateApp app, CancellationToken cancellationToken);
}
