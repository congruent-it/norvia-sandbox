namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>What Rating makes of a Transport Order: either a Freight Charge, or the reason it can't be priced.</summary>
public sealed record RatingOutcome
{
    private RatingOutcome(FreightCharge? freightCharge, RatingFailure? failure)
    {
        FreightCharge = freightCharge;
        Failure = failure;
    }

    /// <summary>Null when Rating failed.</summary>
    public FreightCharge? FreightCharge { get; }

    /// <summary>Null when the Transport Order was priced.</summary>
    public RatingFailure? Failure { get; }

    public static RatingOutcome Priced(FreightCharge freightCharge)
    {
        ArgumentNullException.ThrowIfNull(freightCharge);
        return new(freightCharge, null);
    }

    public static RatingOutcome Failed(RatingFailureCode code, string message) => new(null, new RatingFailure(code, message));
}
