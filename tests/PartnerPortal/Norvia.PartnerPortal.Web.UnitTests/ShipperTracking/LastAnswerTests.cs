using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Web.ShipperTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

public sealed class LastAnswerTests
{
    private static readonly DateTimeOffset First = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = First.AddSeconds(3);

    [Fact]
    public void New_HasNotAnswered()
    {
        var last = new LastAnswer<string>();

        (last.HasAnswered, last.Value, last.FetchedAt, last.Error).Should().Be((false, null, null, null));
    }

    [Fact]
    public void Apply_AFoundAnswer_ReplacesTheLast_AndSaysWhenItWasFetched()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.Found("one"), First);

        last.Apply(TrackingAnswer.Found("two"), Second);

        (last.Value, last.FetchedAt, last.Error).Should().Be(("two", Second, null));
    }

    [Fact]
    public void Apply_AFailure_KeepsTheLastAnswerAndItsFetchedAt_AndSaysWhy()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.Found("one"), First);

        last.Apply(TrackingAnswer.Unavailable<string>("Unavailable.", "corr-1"), Second);

        (last.Value, last.FetchedAt, last.Error, last.ErrorMessageId).Should().Be(("one", First, "Unavailable.", "corr-1"));
    }

    [Fact]
    public void Apply_ARefusal_IsKeptBesideTheLastAnswerToo()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.Found("one"), First);

        last.Apply(TrackingAnswer.Failed<string>(TrackingOutcome.Refused, "Refused."), Second);

        (last.Value, last.Error).Should().Be(("one", "Refused."));
    }

    [Fact]
    public void Apply_AFoundAnswerAfterAFailure_TakesTheErrorAway()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.Unavailable<string>("Unavailable.", "corr-1"), First);

        last.Apply(TrackingAnswer.Found("two"), Second);

        (last.Error, last.ErrorMessageId).Should().Be((null, null));
    }

    [Fact]
    public void Apply_NotFound_IsAnAnswer_NotAFailure_SoTheLastValueGoes()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.Found("one"), First);

        last.Apply(TrackingAnswer.NotFound<string>("Not found."), Second);

        (last.Value, last.NotFoundText, last.Error, last.FetchedAt, last.HasAnswered).Should().Be((null, "Not found.", null, Second, true));
    }

    [Fact]
    public void Apply_AFoundAnswerAfterNotFound_ClearsIt()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.NotFound<string>("Not found."), First);

        last.Apply(TrackingAnswer.Found("one"), Second);

        (last.Value, last.NotFoundText).Should().Be(("one", null));
    }

    [Fact]
    public void Fail_KeepsTheLastAnswer_WithoutAMessageId()
    {
        var last = new LastAnswer<string>();
        last.Apply(TrackingAnswer.Found("one"), First);

        last.Fail("Couldn't be fetched.");

        (last.Value, last.FetchedAt, last.Error, last.ErrorMessageId).Should().Be(("one", First, "Couldn't be fetched.", null));
    }
}
