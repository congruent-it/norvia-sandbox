namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>What became of a re-rate of a Transport Order.</summary>
public enum ReRateOutcome
{
    /// <summary>It was rated again: Rated, or Rating failed with the new reason.</summary>
    ReRated = 1,

    /// <summary>The last re-rate sent again under its key. Nothing changed; the order stands as that re-rate left it.</summary>
    Repeat,

    /// <summary>The order isn't Rating failed, so nothing changed.</summary>
    Refused,
}
