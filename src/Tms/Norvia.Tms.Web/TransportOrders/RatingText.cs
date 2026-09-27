using System.Globalization;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>Words the commercial side of a Transport Order as a Planner reads it, in the glossary's terms.</summary>
public static class RatingText
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>The rating result; empty for a Rejected booking, which has none.</summary>
    public static string Of(RatingResult? result) => result switch
    {
        RatingResult.NotYetRated => "Not yet rated",
        RatingResult.Rated => "Rated",
        RatingResult.RatingFailed => "Rating failed",
        RatingResult.NotRatedSeparately => "Not rated separately",
        _ => string.Empty,
    };

    /// <summary>
    /// What a charge line charges for. The query side carries the stable charge line type; one the UI doesn't know yet
    /// is shown as it came rather than hidden.
    /// </summary>
    public static string ChargeLine(string type) => type switch
    {
        nameof(ChargeLineType.Freight) => "Freight",
        nameof(ChargeLineType.MinimumChargeAdjustment) => "Minimum charge adjustment",
        _ => type,
    };

    /// <summary>An amount of a Freight Charge, which is always in EUR without VAT.</summary>
    public static string Euros(decimal amount) => $"EUR {amount.ToString("#,##0.00", Culture)}";
}
