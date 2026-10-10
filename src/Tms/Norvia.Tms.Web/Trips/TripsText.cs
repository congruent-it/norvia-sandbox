using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Web.Trips;

/// <summary>How the Trips view words a Trip.</summary>
internal static class TripsText
{
    public static string Status(TripStatus status) => status switch
    {
        TripStatus.Draft => "Draft",
        TripStatus.Dispatched => "Dispatched",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown Trip status."),
    };
}
