using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

/// <summary>What a delivery report left the version's state as, or that it was ignored because the version was no longer Sending (P11).</summary>
public sealed record TripDeliveryReportResult(string Number, int Version, TripDeliveryState State, bool Ignored = false);
