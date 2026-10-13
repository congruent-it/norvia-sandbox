using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

public sealed class BookingAnswersTests
{
    [Fact]
    public void Read_Accepted_NamesTheTransportOrder()
    {
        var answer = BookingAnswers.Read(201, """{"result":"ACCEPTED","norviaOrderNo":"TO-0000042","messageId":"corr-1"}""", "header-id");

        answer.Outcome.Should().Be(LogOutcome.Accepted);
        answer.TransportOrderNumber.Should().Be("TO-0000042");
        answer.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void Read_AlreadyBooked_NamesTheOriginalOrder()
    {
        var answer = BookingAnswers.Read(200, """{"result":"ALREADY_BOOKED","norviaOrderNo":"TO-0000042"}""", "header-id");

        answer.Outcome.Should().Be(LogOutcome.AlreadyBooked);
        answer.TransportOrderNumber.Should().Be("TO-0000042");
        answer.CorrelationId.Should().Be("header-id", "the body names none");
    }

    [Fact]
    public void Read_Rejected_KeepsTheCodesAndNotTheTexts()
    {
        var answer = BookingAnswers.Read(
            422,
            """{"result":"REJECTED","errors":[{"code":"ConsigneeNameMissing","text":"Name of Jan"},{"code":"GoodsLinesMissing","text":"x"}]}""",
            "c");

        answer.Outcome.Should().Be(LogOutcome.Rejected);
        answer.Detail.Should().Be("ConsigneeNameMissing, GoodsLinesMissing");
        answer.TransportOrderNumber.Should().BeNull();
    }

    [Theory]
    [InlineData(403, """{"text":"Not a Shipper."}""", "Not a Shipper.")]
    [InlineData(403, "", "Norvia didn't recognise this Persona's subscription key or token.")]
    [InlineData(401, "{}", "Norvia didn't recognise this Persona's subscription key or token.")]
    public void Read_RefusedCredentials_IsRefused(int status, string body, string detail)
    {
        var answer = BookingAnswers.Read(status, body, "c");

        answer.Outcome.Should().Be(LogOutcome.Refused);
        answer.Detail.Should().Be(detail);
    }

    [Theory]
    [InlineData(502, """{"result":"NOT_RECEIVED"}""")]
    [InlineData(201, """{"result":"ACCEPTED"}""")]
    [InlineData(422, """{"result":"REJECTED","errors":[]}""")]
    [InlineData(422, """{"result":"REJECTED","errors":[{"text":"no code"}]}""")]
    [InlineData(200, "not json")]
    public void Read_AnythingElse_IsNotReceived(int status, string body)
    {
        BookingAnswers.Read(status, body, "c").Outcome.Should().Be(LogOutcome.NotReceived);
    }
}
