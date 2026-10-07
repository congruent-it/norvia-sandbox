namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>What the seed file says of a Shipper Persona beyond who it is.</summary>
/// <param name="TrackingPollInterval">How often its tracking screen asks Norvia again while it is open.</param>
public sealed record ShipperProfile(TimeSpan TrackingPollInterval)
{
    /// <summary>The interval of a Shipper Persona the seed file sets none for.</summary>
    public static readonly TimeSpan DefaultTrackingPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>The shortest interval: each poll is a real API Management call, and the Shipper product's rate limit is 120 calls a minute.</summary>
    public static readonly TimeSpan MinimumTrackingPollInterval = TimeSpan.FromSeconds(1);
}
