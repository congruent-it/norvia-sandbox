namespace Norvia.Tms.Domain.TransportOrders;

public enum TransportOrderStatus
{
    /// <summary>The booking passed validation and has a Transport Order number. It waits for Rating (P2) and planning (P3).</summary>
    Accepted,
}
