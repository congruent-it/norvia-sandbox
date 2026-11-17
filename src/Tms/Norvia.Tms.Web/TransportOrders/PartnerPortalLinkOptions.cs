namespace Norvia.Tms.Web.TransportOrders;

/// <summary>Where the Partner portal is, so the Planner can hand a Consignee's tracking link out whole (ADR 0052).</summary>
public sealed class PartnerPortalLinkOptions
{
    public const string SectionName = "PartnerPortal";

    /// <summary>The Partner portal's address, such as <c>https://portal.example</c>. Empty: the link is shown as its path.</summary>
    public string? BaseAddress { get; set; }
}
