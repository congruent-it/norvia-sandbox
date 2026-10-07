using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.ProofsOfDelivery.Read;

namespace Norvia.Tms.Application.ShipperTracking.ReadProofOfDeliveryFile;

/// <summary>
/// Reads one file of a Shipper's Transport Order's current Proof of delivery (P8). A file of a superseded Proof of delivery, a
/// Transport Order with none, or a file the current one doesn't hold is ProofOfDeliveryNotFound.
/// </summary>
/// <param name="ShipperId">The Shipper whose Transport Order is asked for, from the caller's header: another Shipper's is not found.</param>
public sealed record GetShipperProofOfDeliveryFileQuery(string ShipperId, string TransportOrderNumber, Guid FileId) : IQuery<Result<ProofOfDeliveryFileContent>>;
