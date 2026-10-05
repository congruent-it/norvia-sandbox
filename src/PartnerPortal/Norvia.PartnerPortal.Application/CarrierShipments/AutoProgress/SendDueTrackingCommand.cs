using Mediator;

namespace Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;

/// <summary>
/// Sends the automatic Tracking event of every Shipment whose delay has passed, each through the same send use case as the
/// buttons, so each goes through API Management to Norvia.
/// </summary>
public sealed record SendDueTrackingCommand : ICommand<AutoProgressOutcome>;
