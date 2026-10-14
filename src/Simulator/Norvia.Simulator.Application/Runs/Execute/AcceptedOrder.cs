using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A booking Norvia accepted, with the Transport Order number it answered, which the Planner assigns.</summary>
internal sealed record AcceptedOrder(PlannedBooking Booking, string TransportOrderNumber);
