namespace Norvia.Tms.Domain.RateCards;

/// <summary>Which price list a card is: what Norvia charges a Shipper, or what it pays a Carrier.</summary>
public enum RateCardKind
{
    RateCard,
    CarrierRateCard,
}

public static class RateCardKindExtensions
{
    /// <summary>What the card is called in a message, in the domain's words.</summary>
    public static string DisplayName(this RateCardKind kind) => kind switch
    {
        RateCardKind.RateCard => "Rate Card",
        RateCardKind.CarrierRateCard => "Carrier Rate Card",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
