using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;

/// <summary>A file of the Transport Order's current Proof of delivery, as the Shipper Persona downloads it.</summary>
public sealed record DownloadProofOfDeliveryFileQuery(PersonaId PersonaId, string TransportOrderNumber, Guid FileId) : IQuery<TrackingAnswer<ShipperProofFileContent>>;
