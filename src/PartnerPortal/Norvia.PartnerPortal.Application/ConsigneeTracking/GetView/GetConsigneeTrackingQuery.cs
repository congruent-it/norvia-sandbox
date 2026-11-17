using Mediator;
using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Application.ConsigneeTracking.GetView;

/// <summary>
/// The Consignee's view of the Transport Order its link's tracking code opens (ADR 0052). The code is a credential: it is never logged or traced.
/// </summary>
/// <param name="ClientIp">The Consignee's own address, so API Management limits each Consignee and not the portal as a whole; null when unknown.</param>
public sealed record GetConsigneeTrackingQuery(string TrackingCode, string? ClientIp = null) : IQuery<TrackingAnswer<ConsigneeTrackingView>>
{
    /// <summary>Never the code.</summary>
    public override string ToString() => nameof(GetConsigneeTrackingQuery);
}
