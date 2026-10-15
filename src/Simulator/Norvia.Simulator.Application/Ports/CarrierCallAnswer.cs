using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// Norvia's answer to one of a Carrier's calls, as the log shows it. <see cref="Detail"/> holds a refusal's code, never the text Norvia
/// words it with.
/// </summary>
public sealed record CarrierCallAnswer(LogOutcome Outcome, string? CorrelationId, string? Detail);
