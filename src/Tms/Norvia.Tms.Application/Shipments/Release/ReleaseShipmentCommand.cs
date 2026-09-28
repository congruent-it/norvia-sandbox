using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Shipments.Release;

/// <summary>
/// The Planner releases a Planned Shipment, handing it over to be carried out (P3). The Shipment becomes Released, and
/// ShipmentReleased is published with its Transport Order's details, in one transaction. The release starts a Message
/// trail of its own. The release sent again under its key is a repeat, answered as before.
/// </summary>
/// <param name="ReleaseKey">The idempotency key that makes a retry of the release a repeat.</param>
/// <param name="CorrelationId">
/// The new Message trail the TMS starts for this release, as it starts in the TMS UI. A repeat answers with the trail of
/// the release it repeats.
/// </param>
public sealed record ReleaseShipmentCommand(string ShipmentNumber, string ReleaseKey, string CorrelationId)
    : ICommand<Result<ReleaseShipmentResult>>;
