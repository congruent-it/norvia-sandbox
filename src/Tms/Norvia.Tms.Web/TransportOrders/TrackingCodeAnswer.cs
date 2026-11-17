namespace Norvia.Tms.Web.TransportOrders;

/// <summary>What the TMS API answered a read or a replacement of a Transport Order's tracking code (ADR 0052).</summary>
public abstract record TrackingCodeAnswer
{
    private TrackingCodeAnswer()
    {
    }

    /// <summary>The Transport Order's current code; null for an Accepted order from before codes existed.</summary>
    public sealed record Shown(string? Code) : TrackingCodeAnswer
    {
        /// <summary>Never the code: it is a credential.</summary>
        public override string ToString() => nameof(Shown);
    }

    /// <summary>The TMS knows no such Transport Order, or it is a Rejected booking.</summary>
    public sealed record NotFound : TrackingCodeAnswer;
}
