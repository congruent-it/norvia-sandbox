using Mediator;

namespace Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;

/// <summary>A file of the Transport Order's current Proof of delivery, as the Shipper Persona downloads it.</summary>
public sealed record DownloadProofOfDeliveryFileQuery(string PersonaId, string TransportOrderNumber, Guid FileId) : IQuery<TrackingAnswer<ShipperProofFileContent>>;
