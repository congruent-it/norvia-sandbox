using Mediator;

namespace Norvia.PartnerPortal.Application.CarrierShipments.List;

/// <summary>The Carrier Persona's Shipments, Booked or later and Delivered ones too, the latest first.</summary>
public sealed record ListCarrierShipmentsQuery(string PersonaId) : IQuery<IReadOnlyList<CarrierShipmentView>>;
