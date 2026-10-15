using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class CarrierAnswersTests
{
    [Theory]
    [InlineData("CONFIRMED", LogOutcome.Confirmed)]
    [InlineData("DECLINED", LogOutcome.Declined)]
    public void Answered_Recorded_IsTheDecisionNorviaHolds(string decision, LogOutcome expected)
    {
        var answer = CarrierAnswers.Answered(200, $$"""{"result":"RECORDED","messageId":"m-1","carrierBookingNumber":"CB-0000042","decision":"{{decision}}","repeated":false}""", "header-1");

        answer.Should().Be(new Application.Ports.CarrierCallAnswer(expected, "m-1", null));
    }

    [Theory]
    [InlineData(409, "BOOKING_NOT_OPEN")]
    [InlineData(422, "REFERENCE_IN_USE")]
    [InlineData(404, "BOOKING_NOT_FOUND")]
    [InlineData(403, "NOT_ALLOWED")]
    [InlineData(400, "INVALID")]
    public void Answered_ARefusalNorviaGave_IsRefusedWithItsCodeAndNotItsText(int status, string result)
    {
        var answer = CarrierAnswers.Answered(status, $$"""{"result":"{{result}}","messageId":"m-2","text":"Something about CB-0000042"}""", null);

        answer.Outcome.Should().Be(LogOutcome.CarrierRefused);
        answer.CorrelationId.Should().Be("m-2");
        answer.Detail.Should().Be($"{result} ({status})").And.NotContain("Something");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Answered_ARefusalAtTheEdge_IsRefusedWithItsStatus(int status)
    {
        var answer = CarrierAnswers.Answered(status, """{"messageId":"m-3","text":"This client may not answer Carrier bookings."}""", "header-3");

        answer.Outcome.Should().Be(LogOutcome.CarrierRefused);
        answer.CorrelationId.Should().Be("m-3");
        answer.Detail.Should().Be($"Refused at the edge ({status})");
    }

    [Fact]
    public void Answered_NotReceivedByNorvia_IsNotReceived()
    {
        var answer = CarrierAnswers.Answered(502, """{"result":"NOT_RECEIVED","messageId":"m-4","text":"x"}""", null);

        answer.Outcome.Should().Be(LogOutcome.CarrierNotReceived);
        answer.Detail.Should().Be("NOT_RECEIVED (502)");
    }

    [Fact]
    public void Answered_SomethingElse_IsNotReceivedWithTheHeaderCorrelationId()
    {
        var answer = CarrierAnswers.Answered(200, "not json", "header-5");

        answer.Outcome.Should().Be(LogOutcome.CarrierNotReceived);
        answer.CorrelationId.Should().Be("header-5");
        answer.Detail.Should().Contain("200");
    }

    [Fact]
    public void Accepted_202WithAMessageId_IsTheSuccessGiven()
    {
        CarrierAnswers.Accepted(202, """{"messageId":"m-6"}""", null, LogOutcome.TrackingSent).Should().Be(new Application.Ports.CarrierCallAnswer(LogOutcome.TrackingSent, "m-6", null));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(413)]
    public void Accepted_AClientError_IsRefusedWithItsStatus(int status)
    {
        var answer = CarrierAnswers.Accepted(status, """{"messageId":"m-7","text":"no"}""", "h", LogOutcome.TrackingSent);

        answer.Outcome.Should().Be(LogOutcome.CarrierRefused);
        answer.Detail.Should().Be($"Refused ({status})");
        answer.CorrelationId.Should().Be("m-7");
    }

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(200)]
    public void Accepted_AnythingElse_IsNotReceived(int status)
    {
        var answer = CarrierAnswers.Accepted(status, "{}", "h-8", LogOutcome.ProofOfDeliverySent);

        answer.Outcome.Should().Be(LogOutcome.CarrierNotReceived);
        answer.CorrelationId.Should().Be("h-8");
        answer.Detail.Should().Contain(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NotReceived_HasNoCorrelationId()
    {
        CarrierAnswers.NotReceived("It didn't arrive").Should().Be(new Application.Ports.CarrierCallAnswer(LogOutcome.CarrierNotReceived, null, "It didn't arrive"));
    }
}
