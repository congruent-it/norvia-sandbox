using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ConsigneeTracking.GetCode;

/// <summary>The Planner reads a Transport Order's current tracking code.</summary>
public sealed record GetTrackingCodeQuery(string TransportOrderNumber) : IQuery<Result<TransportOrderTrackingCodeView>>;
