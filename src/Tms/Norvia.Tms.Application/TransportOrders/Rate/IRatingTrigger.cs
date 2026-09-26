namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Wakes the rating worker, so a Transport Order is rated moments after its acceptance is committed instead of on the
/// worker's next sweep. A trigger that is missed only delays Rating: the sweep still finds the order.
/// </summary>
public interface IRatingTrigger
{
    void Trigger();
}
