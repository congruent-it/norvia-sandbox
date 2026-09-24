namespace Norvia.Tms.Domain.TransportOrders;

public enum TransportOrderStatus
{
    /// <summary>The booking passed validation and has a Transport Order number. It waits for Rating (P2) and planning (P3).</summary>
    Accepted,

    /// <summary>The booking broke Transport Order rules. It is kept with its reasons and what was submitted, but has no number.</summary>
    Rejected,
}
