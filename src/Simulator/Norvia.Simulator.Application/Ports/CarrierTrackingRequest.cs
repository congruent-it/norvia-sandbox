using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Application.Ports;

/// <summary>One tracking event a Carrier reports: what happened, when (an instant; the Partner API takes it as local time at the place) and where.</summary>
public sealed record CarrierTrackingRequest(
    string BookingReference, string CarrierBookingNumber, string EventId, CarrierEvent Event, DateTimeOffset At, PlannedAddress Place, string? Remark, string? Profile = null);
