using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>The intake Logic App relays the TMS's outcome to the Shipper in the partner format, unchanged in meaning.</summary>
public sealed class BookingResponsesTests
{
    private const string CorrelationId = "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31";
    private const string Booking = """{ "bookingRef": "BD-2030-0002", "receiverName": "Café De Kroon" }""";

    [Fact]
    public void Relay_TmsAccepted_AnswersAcceptedWithTheTransportOrderNumber()
    {
        var answer = BookingResponses.Relay(201, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted", "repeat": false }""", CorrelationId, Booking);

        answer.StatusCode.Should().Be(201);
        ShouldBe(answer.Body, """{ "result": "ACCEPTED", "messageId": "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31", "bookingRef": "BD-2030-0002", "norviaOrderNo": "TO-0000042" }""");
    }

    [Fact]
    public void Relay_TmsRepeat_AnswersAlreadyBookedWithTheOriginalNumber()
    {
        var answer = BookingResponses.Relay(200, """{ "transportOrderNumber": "TO-0000042", "status": "Accepted", "repeat": true }""", CorrelationId, Booking);

        answer.StatusCode.Should().Be(200);
        ShouldBe(answer.Body, """{ "result": "ALREADY_BOOKED", "messageId": "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31", "bookingRef": "BD-2030-0002", "norviaOrderNo": "TO-0000042" }""");
    }

    [Fact]
    public void Relay_TmsRejected_AnswersRejectedWithEveryReason()
    {
        const string problem = """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
              "title": "The Transport Order was rejected.",
              "status": 422,
              "instance": "/v1/transport-orders/0f8fad5b-d9cb-469f-a165-70867728950e",
              "reasons": [
                { "code": "DeliveryAddressIncomplete", "message": "The delivery address has no country." },
                { "code": "WeightInvalid", "message": "Goods line 1 needs a weight greater than 0 kg." }
              ]
            }
            """;

        var answer = BookingResponses.Relay(422, problem, CorrelationId, Booking);

        answer.StatusCode.Should().Be(422);
        ShouldBe(answer.Body, """
            {
              "result": "REJECTED",
              "messageId": "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31",
              "bookingRef": "BD-2030-0002",
              "errors": [
                { "code": "DeliveryAddressIncomplete", "text": "The delivery address has no country." },
                { "code": "WeightInvalid", "text": "Goods line 1 needs a weight greater than 0 kg." }
              ]
            }
            """);
    }

    [Fact]
    public void Relay_TmsAnswerLogicAppsKeptAsContent_ReadsTheAnswerInside()
    {
        // Logic Apps passes a body whose content type it doesn't read as JSON, such as application/problem+json, as
        // base64 content.
        var problem = """{ "status": 422, "reasons": [{ "code": "GoodsLinesMissing", "message": "The booking has no goods lines." }] }""";
        var content = new JsonObject
        {
            ["$content-type"] = "application/problem+json; charset=utf-8",
            ["$content"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(problem)),
        };

        var answer = BookingResponses.Relay(422, content.ToJsonString(), CorrelationId, Booking);

        answer.StatusCode.Should().Be(422);
        answer.Body["errors"]!.ToJsonString().Should().Be("""[{"code":"GoodsLinesMissing","text":"The booking has no goods lines."}]""");
    }

    [Fact]
    public void Relay_BookingWithoutReference_AnswersWithoutOne()
    {
        var answer = BookingResponses.Relay(201, """{ "transportOrderNumber": "TO-0000042" }""", CorrelationId, "not a booking");

        ShouldBe(answer.Body, """{ "result": "ACCEPTED", "messageId": "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31", "norviaOrderNo": "TO-0000042" }""");
    }

    [Theory]
    [InlineData(0, "")] // no answer: the TMS timed out or couldn't be reached
    [InlineData(500, """{ "title": "An error occurred while processing your request.", "status": 500 }""")]
    [InlineData(503, "Service Unavailable")]
    [InlineData(504, "")]
    [InlineData(429, "")]
    [InlineData(400, """{ "title": "One or more validation errors occurred.", "status": 400, "errors": { "correlationId": ["The X-Correlation-ID header is required."] } }""")]
    [InlineData(401, "")]
    [InlineData(403, """{ "title": "The caller is not registered for an intake channel.", "status": 403 }""")]
    public void Relay_TmsDidNotTakeTheBookingIn_AnswersNotReceived(int tmsStatusCode, string tmsResponse)
    {
        var answer = BookingResponses.Relay(tmsStatusCode, tmsResponse, CorrelationId, Booking);

        answer.StatusCode.Should().Be(502);
        ShouldBe(answer.Body, """
            {
              "result": "NOT_RECEIVED",
              "messageId": "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31",
              "bookingRef": "BD-2030-0002",
              "text": "Norvia could not take your booking in, so nothing was booked. Please send it again later."
            }
            """);
    }

    [Theory]
    [InlineData(0, "TmsUnavailable")]
    [InlineData(500, "TmsUnavailable")]
    [InlineData(503, "TmsUnavailable")]
    [InlineData(408, "TmsUnavailable")]
    [InlineData(429, "TmsUnavailable")]
    [InlineData(400, "TmsCallRefused")]
    [InlineData(401, "TmsCallRefused")]
    [InlineData(403, "TmsCallRefused")]
    [InlineData(415, "TmsCallRefused")]
    public void Relay_TmsDidNotTakeTheBookingIn_SaysWhyForTheRun(int tmsStatusCode, string failureCode)
    {
        // The TMS couldn't be reached, or the integration layer sent something it won't take (coding guidelines §7).
        var answer = BookingResponses.Relay(tmsStatusCode, "", CorrelationId, Booking);

        answer.FailureCode.Should().Be(failureCode);
    }

    [Theory]
    [InlineData(201, """{ "transportOrderNumber": "TO-0000042" }""")]
    [InlineData(200, """{ "transportOrderNumber": "TO-0000042" }""")]
    [InlineData(422, """{ "reasons": [{ "code": "GoodsLinesMissing", "message": "The booking has no goods lines." }] }""")]
    public void Relay_TmsTookTheBookingIn_HasNoFailureCode(int tmsStatusCode, string tmsResponse)
    {
        BookingResponses.Relay(tmsStatusCode, tmsResponse, CorrelationId, Booking).FailureCode.Should().BeNull();
    }

    [Fact]
    public void ToJsonString_NotReceived_GivesTheStatusBodyAndFailureCode()
    {
        // The Logic App answers the Shipper with the status and body, and fails the run with the failure code.
        var json = BookingResponses.Relay(503, "", CorrelationId, Booking).ToJsonString();

        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse("""
            {
              "statusCode": 502,
              "body": {
                "result": "NOT_RECEIVED",
                "text": "Norvia could not take your booking in, so nothing was booked. Please send it again later.",
                "messageId": "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31",
                "bookingRef": "BD-2030-0002"
              },
              "failureCode": "TmsUnavailable"
            }
            """)).Should().BeTrue(json);
    }

    [Fact]
    public void ToJsonString_Accepted_LeavesTheFailureCodeOut()
    {
        var json = BookingResponses.Relay(201, """{ "transportOrderNumber": "TO-0000042" }""", CorrelationId, Booking).ToJsonString();

        JsonNode.Parse(json)!.AsObject().Select(property => property.Key).Should().BeEquivalentTo("statusCode", "body");
    }

    [Theory]
    [InlineData(201, """{ "status": "Accepted", "repeat": false }""")]
    [InlineData(200, "")]
    [InlineData(422, """{ "title": "The Transport Order was rejected.", "status": 422, "reasons": [] }""")]
    [InlineData(422, """{ "reasons": [{ "code": "WeightInvalid" }] }""")]
    public void Relay_TmsAnswerItCantPassOnInFull_AnswersNotReceived(int tmsStatusCode, string tmsResponse)
    {
        var answer = BookingResponses.Relay(tmsStatusCode, tmsResponse, CorrelationId, Booking);

        answer.StatusCode.Should().Be(502);
        answer.Body["result"]!.GetValue<string>().Should().Be("NOT_RECEIVED");
        Schemas.PartnerBookingResponse.Validate(answer.Body).IsValid.Should().BeTrue();
    }

    private static void ShouldBe(JsonObject body, string expected)
    {
        var expectedBody = JsonNode.Parse(expected);
        JsonNode.DeepEquals(body, expectedBody).Should().BeTrue($"the answer should be{Environment.NewLine}{expectedBody}{Environment.NewLine}but was{Environment.NewLine}{body}");
        var results = Schemas.PartnerBookingResponse.Validate(body);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }
}
