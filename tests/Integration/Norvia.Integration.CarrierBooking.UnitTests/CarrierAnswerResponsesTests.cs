using System.Text;
using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>The relay of the TMS's response to the Carrier, in the Carrier answer response format.</summary>
public sealed class CarrierAnswerResponsesTests
{
    private const string Correlation = "5b1c2a52-6a52-4d8e-9d52-0d8f6f3f7b11";

    private static readonly MappedCarrierAnswer Confirmed = CarrierAnswerMap.ToCarrierBookingAnswer(
        """{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "R-1" }""", "CAR-0001");

    private static BookingResponse Relay(int status, string body) => CarrierAnswerResponses.Relay(Confirmed, status, body, Correlation);

    private static void ShouldKeepToTheSchema(BookingResponse response)
    {
        var results = Schemas.CarrierAnswerResponse.Validate(response.Body);
        results.IsValid.Should().BeTrue($"{response.Body.ToJsonString()}: {Schemas.Errors(results)}");
    }

    [Fact]
    public void Relay_ARecordedConfirmation_IsRecordedAsConfirmed()
    {
        var response = Relay(200, """{ "carrierBookingNumber": "CB-0000042", "status": "Confirmed", "shipmentNumber": "SH-0000007", "shipmentStatus": "Booked", "correlationId": "x", "repeat": false }""");

        response.StatusCode.Should().Be(200);
        response.FailureCode.Should().BeNull();
        JsonNode.DeepEquals(response.Body, JsonNode.Parse("""{ "result": "RECORDED", "messageId": "5b1c2a52-6a52-4d8e-9d52-0d8f6f3f7b11", "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "repeated": false }"""))
            .Should().BeTrue(response.Body.ToJsonString());
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_ARepeatedRejection_IsRecordedAsDeclinedAndRepeated()
    {
        var response = Relay(200, """{ "carrierBookingNumber": "CB-0000042", "status": "Rejected", "repeat": true }""");

        response.Body["decision"]!.GetValue<string>().Should().Be("DECLINED");
        response.Body["repeated"]!.GetValue<bool>().Should().BeTrue();
        ShouldKeepToTheSchema(response);
    }

    [Theory]
    [InlineData(403, "NOT_ALLOWED")]
    [InlineData(404, "BOOKING_NOT_FOUND")]
    [InlineData(409, "BOOKING_NOT_OPEN")]
    public void Relay_ARefusalOfTheTms_IsRelayedWithItsOwnStatus_AndTheRunNeedNotFail(int status, string result)
    {
        var response = Relay(status, "{}");

        response.StatusCode.Should().Be(status);
        response.Body["result"]!.GetValue<string>().Should().Be(result);
        response.Body["carrierBookingNumber"]!.GetValue<string>().Should().Be("CB-0000042");
        response.FailureCode.Should().BeNull();
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_TheMessageId_IsTheCorrelationIdOfTheAnswer()
    {
        Relay(409, "{}").Body["messageId"]!.GetValue<string>().Should().Be(Correlation);
    }

    [Theory]
    [InlineData(0, "TmsUnavailable")]
    [InlineData(500, "TmsUnavailable")]
    [InlineData(503, "TmsUnavailable")]
    [InlineData(408, "TmsUnavailable")]
    [InlineData(429, "TmsUnavailable")]
    [InlineData(400, "TmsCallRefused")]
    [InlineData(401, "TmsCallRefused")]
    [InlineData(422, "TmsCallRefused")]
    public void Relay_ATmsThatCouldntTakeTheAnswerIn_IsNotReceived_WithTheReasonForTheRun(int status, string failureCode)
    {
        var response = Relay(status, "");

        response.StatusCode.Should().Be(502);
        response.Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        response.FailureCode.Should().Be(failureCode);
        ShouldKeepToTheSchema(response);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "status": "Withdrawn" }""")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000099", "status": "Confirmed" }""")]
    [InlineData("""{ "status": "Confirmed" }""")]
    public void Relay_A200ItCantPassOnInFull_IsNotReceived(string body)
    {
        var response = Relay(200, body);

        response.StatusCode.Should().Be(502);
        response.Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
    }

    [Fact]
    public void Relay_ABodyLogicAppsPassesAsBase64Content_IsRead()
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{ "carrierBookingNumber": "CB-0000042", "status": "Confirmed" }"""));

        Relay(200, $$"""{ "$content-type": "application/problem+json", "$content": "{{content}}" }""").Body["result"]!.GetValue<string>().Should().Be("RECORDED");
        Relay(200, """{ "$content": "%%%" }""").Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
    }

    [Fact]
    public void Relay_AnAnswerThatCantBeUsed_IsInvalid_WithoutTheTmsHavingBeenCalled()
    {
        var invalid = CarrierAnswerMap.ToCarrierBookingAnswer("""{ "carrierBookingNumber": "CB-0000042", "decision": "MAYBE" }""", "CAR-0001");

        var response = CarrierAnswerResponses.Relay(invalid, 0, "", Correlation);

        response.StatusCode.Should().Be(400);
        response.Body["result"]!.GetValue<string>().Should().Be("INVALID");
        response.Body["text"]!.GetValue<string>().Should().Contain("CONFIRMED or DECLINED");
        response.FailureCode.Should().Be("CarrierAnswerUnreadable");
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_AnUnusableAnswerWithoutABookingNumber_LeavesItOut()
    {
        var response = CarrierAnswerResponses.Relay(CarrierAnswerMap.ToCarrierBookingAnswer("{}", "CAR-0001"), 0, "", Correlation);

        response.Body.ContainsKey("carrierBookingNumber").Should().BeFalse();
        ShouldKeepToTheSchema(response);
    }

    [Fact]
    public void Relay_WithoutAMappedAnswer_Throws()
    {
        var relay = () => CarrierAnswerResponses.Relay(null!, 200, "", Correlation);

        relay.Should().Throw<ArgumentNullException>();
    }

    public static TheoryData<string> Responses => new(Contracts.FileNames("PartnerApi/samples/carrier-answer-responses"));

    [Theory]
    [MemberData(nameof(Responses))]
    public void Validate_SampleResponse_KeepsToTheCarrierAnswerResponseSchema(string file)
    {
        var results = Schemas.CarrierAnswerResponse.Validate(JsonNode.Parse(Contracts.Read($"PartnerApi/samples/carrier-answer-responses/{file}")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }
}
