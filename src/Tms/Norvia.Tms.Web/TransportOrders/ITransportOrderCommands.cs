using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Web.Exceptions;
using Norvia.Tms.Web.Shipments;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// What the TMS UI changes about Transport Orders and their Shipments. It reads through the query side in process, but
/// changes things only through the TMS API, as any other caller does.
/// </summary>
public interface ITransportOrderCommands
{
    /// <summary>Re-rates a Transport Order whose Rating failed, typically after its Rate Card was corrected.</summary>
    Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Assigns one Transport Order to Own fleet or to a Carrier, which plans its one Shipment. A multi-select assigns
    /// each Transport Order with a call of its own, so each succeeds or is refused on its own.
    /// </summary>
    /// <param name="carrierId">The Carrier to carry it; null for Own fleet.</param>
    Task<AssignAnswer> AssignAsync(string transportOrderNumber, string? carrierId, CancellationToken cancellationToken);

    /// <summary>Reassigns a Planned Shipment to Own fleet or another Carrier, to fix a mistake before release.</summary>
    /// <param name="carrierId">The Carrier to carry it; null for Own fleet.</param>
    Task<ReassignAnswer> ReassignAsync(string shipmentNumber, string? carrierId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases one Planned Shipment, handing it over to its Carrier or to Own fleet's Trips. A multi-select releases
    /// each Shipment with a call of its own, so each succeeds or is refused on its own.
    /// </summary>
    Task<ReleaseAnswer> ReleaseAsync(string shipmentNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws one Requested Carrier booking before the Carrier answers, which puts its Shipment back to Planned. Each
    /// withdrawal is a call of its own, so one is refused on its own.
    /// </summary>
    Task<WithdrawAnswer> WithdrawAsync(string carrierBookingNumber, CancellationToken cancellationToken);

    /// <summary>Notifies the Shipper from an open Exception, with the message the Planner wrote, starting from the template for its type (P7).</summary>
    Task<ExceptionAnswer> NotifyShipperAsync(string exceptionNumber, string message, CancellationToken cancellationToken);

    /// <summary>Cancels the Confirmed Carrier booking of the Shipment of an open Exception, with a reason, so the Shipment goes back to Planned (P7).</summary>
    Task<ExceptionAnswer> CancelCarrierBookingAsync(string exceptionNumber, string reason, CancellationToken cancellationToken);

    /// <summary>Agrees a delivery window for the Shipment of an open Exception, after agreeing a redelivery with the Carrier outside the TMS (P7).</summary>
    Task<ExceptionAnswer> AgreeDeliveryWindowAsync(string exceptionNumber, DateTimeOffset earliest, DateTimeOffset latest, CancellationToken cancellationToken);

    /// <summary>Adds a free-text note to an open Exception (P7).</summary>
    Task<ExceptionAnswer> AddNoteAsync(string exceptionNumber, string text, CancellationToken cancellationToken);

    /// <summary>Resolves an open Exception with an outcome and the remark every outcome needs (P7).</summary>
    Task<ExceptionAnswer> ResolveAsync(string exceptionNumber, ExceptionOutcome outcome, string remark, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a Delivered, Not yet billable Transport Order without a Proof of delivery, with the remark every release
    /// needs (P9). The order becomes Billable only when nothing else blocks it.
    /// </summary>
    Task<ReleaseWithoutProofOfDeliveryAnswer> ReleaseWithoutProofOfDeliveryAsync(string transportOrderNumber, string remark, CancellationToken cancellationToken);
}
