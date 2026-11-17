namespace Norvia.Tms.Application.ConsigneeTracking;

/// <summary>
/// The tracking code the Planner can hand to a Shipper or Consignee (ADR 0052). It is a credential, so it has a query of its own
/// and is not part of the Transport Order's view, which agents and lists also read. Null for an Accepted Transport Order from
/// before codes existed, until the Planner replaces it.
/// </summary>
public sealed record TransportOrderTrackingCodeView(string TransportOrderNumber, string? TrackingCode)
{
    /// <summary>Never the code.</summary>
    public override string ToString() => nameof(TransportOrderTrackingCodeView);
}
