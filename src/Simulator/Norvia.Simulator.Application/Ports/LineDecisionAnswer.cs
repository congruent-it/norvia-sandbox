using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The ERP's answer to Finance accepting or rejecting a Carrier invoice line, as the log shows it. <see cref="CorrelationId"/> is the Carrier invoice's own trail.
/// <see cref="Detail"/> holds a refusal's code, never the message the ERP words it with.
/// </summary>
public sealed record LineDecisionAnswer(LogOutcome Outcome, string? CorrelationId, string? Detail);
