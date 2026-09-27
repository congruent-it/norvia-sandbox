using Norvia.Tms.Application.TransportOrders;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>What the TMS API answered a re-rate.</summary>
public abstract record ReRateAnswer
{
    private ReRateAnswer()
    {
    }

    /// <summary>The Transport Order is Rated now, so it leaves the rating exceptions.</summary>
    public sealed record Rated : ReRateAnswer;

    /// <summary>Rating failed again, so the Transport Order stays a rating exception, with this failure.</summary>
    public sealed record FailedAgain(RatingFailureView Failure) : ReRateAnswer;

    /// <summary>The TMS didn't re-rate it, such as an order another re-rate has already Rated.</summary>
    public sealed record Refused(string Reason) : ReRateAnswer;
}
