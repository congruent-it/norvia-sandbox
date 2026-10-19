using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>The ERP's answer to a Credit note, as the log shows it. <see cref="Detail"/> holds a refusal's code, never the message the ERP words it with.</summary>
public sealed record CreditNoteAnswer(LogOutcome Outcome, string? CorrelationId, string? CreditNoteNumber, string? Detail);
