using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;

/// <summary>
/// Changes how the Carrier Persona sends Tracking events for the Shipments it confirms from now on; a Shipment already
/// confirmed keeps the mode it was confirmed with. Not found when there is no such Carrier Persona, invalid when the delay is out of range.
/// </summary>
public sealed record ChangeCarrierTrackingSettingsCommand(PersonaId PersonaId, TrackingMode Mode, int DelaySeconds)
    : ICommand<Result<CarrierTrackingSettingsView>>;
