using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>What the TMS API answered a release without a Proof of delivery (P9).</summary>
public abstract record ReleaseWithoutProofOfDeliveryAnswer
{
    private ReleaseWithoutProofOfDeliveryAnswer()
    {
    }

    /// <summary>
    /// The release is recorded. The order is Billable now, or still Not yet billable when something else, such as an open
    /// Exception, still blocks it.
    /// </summary>
    public sealed record Released(BillingState BillingState) : ReleaseWithoutProofOfDeliveryAnswer;

    /// <summary>The TMS didn't release it, such as an order released already, with its readable reason.</summary>
    public sealed record Refused(string Reason) : ReleaseWithoutProofOfDeliveryAnswer;
}
