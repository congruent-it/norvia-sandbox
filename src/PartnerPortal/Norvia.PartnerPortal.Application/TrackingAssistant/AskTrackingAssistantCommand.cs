using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>One question from a Shipper Persona about its own Transport Orders. <paramref name="OpenOrderNumber"/> is the order open on the screen, if any.</summary>
public sealed record AskTrackingAssistantCommand(PersonaId PersonaId, string Question, IReadOnlyList<AssistantTurn> History, string? OpenOrderNumber)
    : ICommand<AssistantAnswer>;
