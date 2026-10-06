namespace Norvia.Tms.Domain.Exceptions;

/// <summary>How the Planner resolved an Exception (P7). Any outcome can be chosen for any type. The names are stable: never rename one, only add.</summary>
public enum ExceptionOutcome
{
    /// <summary>Nothing needed doing.</summary>
    NoActionNeeded = 1,

    /// <summary>The Carrier delivered on another attempt, or the Own fleet on a Re-delivery.</summary>
    Redelivered,

    /// <summary>The Carrier booking was cancelled, or the Own fleet Shipment taken off its Trip, and the Shipment went back to planning.</summary>
    Replanned,

    /// <summary>The goods go back to the Shipper. The return itself isn't planned in the TMS.</summary>
    ReturnedToShipper,

    /// <summary>Damage or a shortage is being settled as a claim outside the TMS.</summary>
    ClaimHandledOutsideTms,
}
