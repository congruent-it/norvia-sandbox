using Mediator;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.List;

/// <summary>The Carrier Persona's Shipments, Booked or later and Delivered ones too, the latest first.</summary>
public sealed record ListCarrierShipmentsQuery(PersonaId PersonaId) : IQuery<IReadOnlyList<CarrierShipmentView>>;
