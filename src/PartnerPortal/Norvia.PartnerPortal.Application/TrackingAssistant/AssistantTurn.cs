namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>One earlier question and the answer the Shipper was shown, kept in the Persona's own circuit and sent with the next question (ADR 0049).</summary>
public sealed record AssistantTurn(string Question, string Answer);
