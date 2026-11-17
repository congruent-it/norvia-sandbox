using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ConsigneeTracking.GetView;

/// <summary>
/// A Consignee's view of the one Transport Order its tracking code opens (ADR 0052). The code is a credential: it is never logged.
/// </summary>
/// <param name="TrackingCode">The code as the Consignee has it, from the link.</param>
public sealed record GetConsigneeViewQuery(string TrackingCode) : IQuery<Result<ConsigneeView>>
{
    /// <summary>Never the code.</summary>
    public override string ToString() => nameof(GetConsigneeViewQuery);
}
