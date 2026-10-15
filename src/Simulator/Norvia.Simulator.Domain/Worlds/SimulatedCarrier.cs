namespace Norvia.Simulator.Domain.Worlds;

/// <summary>
/// A Carrier the Planner can assign to, from the seed file. Only active Carriers are chosen: the TMS refuses an inactive one. The
/// Carrier plays its part (answers, tracks, delivers) as its Persona, whose booking reference prefix starts the references it confirms with.
/// </summary>
public sealed record SimulatedCarrier(string Id, string Name, bool Active, string? PersonaId = null, string? PersonaName = null, string? BookingReferencePrefix = null)
{
    public bool HasPersona => PersonaId is not null && BookingReferencePrefix is not null;
}
