using System.Text.Json.Serialization;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What a cancellation or withdrawal from Norvia did to the Carrier's inbox. The Carrier can't refuse, so every outcome is acknowledged.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CarrierCancellationOutcome
{
    /// <summary>The booking in the inbox changed.</summary>
    Applied,

    /// <summary>The inbox already showed the booking ended by Norvia in this way, so nothing changed.</summary>
    Repeated,

    /// <summary>The booking isn't one this kind of message ends, or the inbox never had it, so nothing changed.</summary>
    NotApplicable,
}
