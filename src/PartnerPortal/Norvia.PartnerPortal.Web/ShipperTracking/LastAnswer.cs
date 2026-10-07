using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>
/// What a tracking screen shows of the answers it polls for. A found answer replaces the last, and says when it was
/// fetched. A failure leaves the last answer where it is, with the time it was fetched, and shows the error beside it
/// (P8). That the Transport Order isn't there is an answer too, not a failure.
/// </summary>
public sealed class LastAnswer<T>
    where T : class
{
    /// <summary>The last answer that was found; null before the first, and after Norvia said there is none.</summary>
    public T? Value { get; private set; }

    /// <summary>When <see cref="Value"/> was fetched, or when Norvia said there is none; null before the first answer.</summary>
    public DateTimeOffset? FetchedAt { get; private set; }

    /// <summary>The words for the last failure, or null when the last poll got an answer.</summary>
    public string? Error { get; private set; }

    /// <summary>The correlation ID Norvia gave the failed query, by which Norvia finds it; null when it never answered.</summary>
    public string? ErrorMessageId { get; private set; }

    /// <summary>Norvia's words when it said there is nothing to show, instead of a value; null otherwise.</summary>
    public string? NotFoundText { get; private set; }

    public bool HasAnswered => FetchedAt is not null;

    public void Apply(TrackingAnswer<T> answer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(answer);

        switch (answer.Outcome)
        {
            case TrackingOutcome.Found:
                Value = answer.Value;
                FetchedAt = now;
                Error = null;
                ErrorMessageId = null;
                NotFoundText = null;
                break;
            case TrackingOutcome.NotFound:
                Value = null;
                FetchedAt = now;
                Error = null;
                ErrorMessageId = null;
                NotFoundText = answer.Text;
                break;
            default:
                Error = answer.Text;
                ErrorMessageId = answer.MessageId;
                break;
        }
    }

    /// <summary>A poll that threw: the same as Norvia being unavailable.</summary>
    public void Fail(string text)
    {
        Error = text;
        ErrorMessageId = null;
    }
}
