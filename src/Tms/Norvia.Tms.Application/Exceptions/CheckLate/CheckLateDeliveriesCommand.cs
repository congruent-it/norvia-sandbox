using Mediator;

namespace Norvia.Tms.Application.Exceptions.CheckLate;

/// <summary>The time-based check (P5, P7): opens a Late delivery Exception for each Shipment on its way whose delivery window has passed, with the grace.</summary>
public sealed record CheckLateDeliveriesCommand : ICommand;
