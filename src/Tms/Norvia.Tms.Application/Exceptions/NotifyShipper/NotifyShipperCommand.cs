using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

/// <summary>
/// From an open Exception the Planner notifies the Shipper, as often as needed, with a message that started from the template for
/// the Exception's type (P7). The TMS records a Shipper notification, Requested, and publishes ShipperNotificationRequested. The
/// action starts in the TMS UI, so its correlation ID is the TMS's own.
/// </summary>
public sealed record NotifyShipperCommand(string ExceptionNumber, string Message, string Planner, string CorrelationId)
    : ICommand<Result<NotifyShipperResult>>;
