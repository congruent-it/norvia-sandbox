using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Application.UnitTests.TrackingAssistant;

public sealed class AnswerCheckTests
{
    private static AssistantReply Reply(string answer, params string[] fields) =>
        new(answer, [.. fields.Select(field => new AssistantClaim(field))], NoInformation: false, TokensUsed: 100);

    [Fact]
    public void Passes_WhenEveryClaimAndValueIsInTheInput()
    {
        var reply = Reply("Order TO-0000042 is In transit. The ETA is 02-10-2030 11:30, last updated 01-10-2030 14:05.", "detail.eta", "detail.lastUpdate", "detail.status");

        AnswerCheck.Passes(reply, TrackingFixtures.Input()).Should().BeTrue();
    }

    [Fact]
    public void Passes_ForANoInformationReply_WithoutClaims()
    {
        var reply = new AssistantReply("I have no information about that.", [], NoInformation: true, TokensUsed: 10);

        AnswerCheck.Passes(reply, TrackingFixtures.Input()).Should().BeTrue();
    }

    [Fact]
    public void Passes_Fails_WhenACitedFieldIsMissing()
    {
        AnswerCheck.Passes(Reply("It is in transit.", "detail.price"), TrackingFixtures.Input()).Should().BeFalse();
    }

    [Fact]
    public void Passes_Fails_WhenAnAnswerHasNoClaimAndIsNotNoInformation()
    {
        AnswerCheck.Passes(Reply("It is in transit."), TrackingFixtures.Input()).Should().BeFalse();
    }

    [Fact]
    public void Passes_Fails_ForAnInventedEta()
    {
        AnswerCheck.Passes(Reply("It arrives 03-10-2030 10:00.", "detail.eta"), TrackingFixtures.Input()).Should().BeFalse();
    }

    [Fact]
    public void Passes_Fails_ForAnInventedTimeOnAKnownDate()
    {
        AnswerCheck.Passes(Reply("The ETA is 02-10-2030 11:45.", "detail.eta"), TrackingFixtures.Input()).Should().BeFalse();
    }

    [Fact]
    public void Passes_Fails_ForAnInventedPrice()
    {
        AnswerCheck.Passes(Reply("The delivery costs 250 euro.", "detail.status"), TrackingFixtures.Input()).Should().BeFalse();
    }

    [Fact]
    public void Passes_Fails_ForANumberFromAnotherOrder()
    {
        AnswerCheck.Passes(Reply("Order TO-0000099 is in transit.", "detail.status"), TrackingFixtures.Input()).Should().BeFalse();
    }

    [Fact]
    public void Passes_Accepts_TheCurrentDateAndTimeFromTheInput()
    {
        AnswerCheck.Passes(Reply("As of 01-10-2030 12:00 the order is in transit.", "detail.status"), TrackingFixtures.Input()).Should().BeTrue();
    }

    [Fact]
    public void Passes_ResolvesIndexedPaths()
    {
        AnswerCheck.Passes(Reply("Collected at 01-10-2030 09:10.", "detail.progress[0].time"), TrackingFixtures.Input()).Should().BeTrue();
        AnswerCheck.Passes(Reply("Collected at 01-10-2030 09:10.", "detail.progress[3].time"), TrackingFixtures.Input()).Should().BeFalse();
    }
}
