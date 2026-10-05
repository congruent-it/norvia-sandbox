using Ardalis.Result;
using Mediator;

namespace Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;

/// <summary>How the Carrier Persona sends Tracking events now. Not found when there is no such Carrier Persona.</summary>
public sealed record GetCarrierTrackingSettingsQuery(string PersonaId) : IQuery<Result<CarrierTrackingSettingsView>>;
