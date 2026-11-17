using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ConsigneeTracking.ReplaceCode;

/// <summary>
/// The Planner replaces a Transport Order's tracking code (ADR 0052), because a link went to the wrong person. The old code stops
/// working at once. There is no revocation without a replacement.
/// </summary>
public sealed record ReplaceTrackingCodeCommand(string TransportOrderNumber) : ICommand<Result<TransportOrderTrackingCodeView>>;
