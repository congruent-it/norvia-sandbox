using Mediator;

namespace Norvia.PartnerPortal.Application.CarrierShipments.List;

/// <summary>The Carrier Persona's Booked, Collected and In transit Shipments, the latest first.</summary>
public sealed record ListCarrierShipmentsQuery(string PersonaId) : IQuery<IReadOnlyList<CarrierShipmentView>>;
