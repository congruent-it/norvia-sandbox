using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Drives rejected bookings through TMS intake the way the intake Logic App does, and checks only what an outside
/// observer can see: the HTTP response, the query API and the events on the Transport Order events topic.
/// </summary>
public sealed class TransportOrderRejectionTests(TmsFixture tms)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OutboxSettleTime = TimeSpan.FromSeconds(3);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    public static TheoryData<string> EveryRule() =>
    [
        "UnknownShipper",
        "InactiveShipper",
        "ShipperReferenceMissing",
        "CollectionAddressMissing",
        "CollectionAddressIncomplete",
        "DeliveryAddressMissing",
        "DeliveryAddressIncomplete",
        "ConsigneeNameMissing",
        "CollectionWindowMissing",
        "CollectionWindowInPast",
        "CollectionWindowReversed",
        "DeliveryWindowIncomplete",
        "DeliveryWindowReversed",
        "DeliveryWindowBeforeCollection",
        "GoodsLinesMissing",
        "PackageTypeInvalid",
        "QuantityInvalid",
        "WeightInvalid",
        "DimensionsInvalid",
        "TotalWeightOverLimit",
        "ShipperReferenceTooLong",
    ];

    [Theory]
    [MemberData(nameof(EveryRule))]
    public async Task Submit_BookingBreakingOneRule_IsRejectedWithThatRulesCodeAndAMessage(string code)
    {
        var booking = Bookings.Valid(NewReference());
        BreakRule(booking, code);

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(_cancellationToken));
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var reason = (await ReadJsonAsync(response))["reasons"]!.AsArray().Should().ContainSingle().Subject!;
        reason["code"]!.GetValue<string>().Should().Be(code);
        reason["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Submit_AddressWithABlankCountry_IsRejectedAsIncompleteNotRefusedAsMalformed()
    {
        var booking = Bookings.Valid(NewReference());
        booking["collection"]!["address"]!["country"] = "";

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        CodesOf(await ReadJsonAsync(response)).Should().Equal("CollectionAddressIncomplete");
    }

    [Fact]
    public async Task Submit_BookingBreakingSeveralRules_GetsEveryFailureInOneResponse()
    {
        var booking = Bookings.Valid(NewReference());
        BreakRule(booking, "InactiveShipper");
        BreakRule(booking, "ConsigneeNameMissing");
        BreakRule(booking, "QuantityInvalid");

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        CodesOf(await ReadJsonAsync(response)).Should().Equal("InactiveShipper", "ConsigneeNameMissing", "QuantityInvalid");
    }

    [Fact]
    public async Task Submit_TotalWeightExactlyAtTheDefaultLimit_IsAccepted()
    {
        var booking = Bookings.Valid(NewReference());
        booking["goodsLines"]![0]!["weightKg"] = 23_940;

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the default limit is 24,000 kg and the goods weigh exactly that");
    }

    [Fact]
    public async Task Submit_RejectedBooking_CanBeReadWithItsReasonsAndSubmittedContentButNoNumber()
    {
        var booking = Bookings.Valid(NewReference());
        BreakRule(booking, "GoodsLinesMissing");
        var correlationId = NewCorrelationId();
        using var rejection = await SubmitAsync(booking, correlationId);
        var problem = await ReadJsonAsync(rejection);

        using var response = await tms.Tms.GetAsync(new Uri(problem["instance"]!.GetValue<string>(), UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await ReadJsonAsync(response);
        rejected["status"]!.GetValue<string>().Should().Be("Rejected");
        rejected["transportOrderNumber"].Should().BeNull();
        rejected["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        rejected["rejectionReasons"]!.AsArray().Should().ContainSingle()
            .Which!["code"]!.GetValue<string>().Should().Be("GoodsLinesMissing");
        JsonNode.DeepEquals(rejected["transportOrder"]!["goodsLines"], new JsonArray()).Should().BeTrue();
        rejected["transportOrder"]!["shipperReference"]!.GetValue<string>().Should().Be(booking["shipperReference"]!.GetValue<string>());
        rejected["transportOrder"]!.AsObject().ContainsKey("transportOrderNumber").Should().BeFalse();
    }

    [Fact]
    public async Task Submit_RejectedBooking_IsListedWithTheRejectedBookingsButNotTheAcceptedOnes()
    {
        var reference = NewReference();
        var booking = Bookings.Valid(reference);
        BreakRule(booking, "UnknownShipper");
        using var rejection = await SubmitAsync(booking, NewCorrelationId());
        rejection.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var rejected = await ListAsync("Rejected");
        var accepted = await ListAsync("Accepted");

        var listed = rejected.Single(o => o!["shipperReference"]?.GetValue<string>() == reference)!;
        listed["status"]!.GetValue<string>().Should().Be("Rejected");
        listed["transportOrderNumber"].Should().BeNull();
        listed["shipper"]!["id"]!.GetValue<string>().Should().Be("SHP-9999");
        accepted.Should().NotContain(o => o!["shipperReference"]!.GetValue<string>() == reference);
    }

    [Fact]
    public async Task Submit_RejectedBooking_PublishesExactlyOneTransportOrderRejected()
    {
        var reference = NewReference();
        var booking = Bookings.Valid(reference);
        BreakRule(booking, "InactiveShipper");
        BreakRule(booking, "PackageTypeInvalid");
        var correlationId = NewCorrelationId();
        using var rejection = await SubmitAsync(booking, correlationId);
        rejection.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var events = await PublishedEventsAsync(correlationId);

        var message = events.Should().ContainSingle().Subject;
        message.Subject.Should().Be("TransportOrderRejected");
        message.CorrelationId.Should().Be(correlationId);
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["flow"] = "transport-order-intake",
            ["partyId"] = "SHP-0002",
            ["eventType"] = "TransportOrderRejected",
        });
        message.ApplicationProperties.Should().NotContainKey("transportOrderNumber");

        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateTransportOrderRejected(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        body["shipperReference"]!.GetValue<string>().Should().Be(reference);
        CodesOf(body).Should().Equal("InactiveShipper", "PackageTypeInvalid");
    }

    private static void BreakRule(JsonObject booking, string code)
    {
        var collection = booking["collection"]!;
        var delivery = booking["delivery"]!;
        var firstLine = booking["goodsLines"]![0]!;
        switch (code)
        {
            case "UnknownShipper": booking["shipperId"] = "SHP-9999"; break;
            case "InactiveShipper": booking["shipperId"] = "SHP-0002"; break;
            case "ShipperReferenceMissing": booking["shipperReference"] = null; break;
            case "CollectionAddressMissing": collection["address"] = null; break;
            case "CollectionAddressIncomplete": collection["address"]!["street"] = null; break;
            case "DeliveryAddressMissing": delivery["address"] = null; break;
            case "DeliveryAddressIncomplete": delivery["address"]!["postcode"] = ""; break;
            case "ConsigneeNameMissing": delivery["consignee"]!["name"] = null; break;
            case "CollectionWindowMissing": collection["window"] = null; break;
            case "CollectionWindowInPast": collection["window"]!["earliest"] = DateTimeOffset.UtcNow.AddHours(-1).ToString("O"); break;
            case "CollectionWindowReversed": collection["window"]!["earliest"] = LaterBy(collection["window"]!["latest"]!, hours: 1); break;
            case "DeliveryWindowIncomplete": delivery["window"]!["latest"] = null; break;
            case "DeliveryWindowReversed": delivery["window"]!["earliest"] = LaterBy(delivery["window"]!["latest"]!, hours: 1); break;
            case "DeliveryWindowBeforeCollection":
                delivery["window"] = new JsonObject
                {
                    ["earliest"] = LaterBy(collection["window"]!["earliest"]!, hours: -3),
                    ["latest"] = LaterBy(collection["window"]!["earliest"]!, hours: -1),
                };
                break;
            case "GoodsLinesMissing": booking["goodsLines"] = new JsonArray(); break;
            case "PackageTypeInvalid": firstLine["packageType"] = "barrel"; break;
            case "QuantityInvalid": firstLine["quantity"] = 0; break;
            case "WeightInvalid": firstLine["weightKg"] = 0; break;
            case "DimensionsInvalid": firstLine["dimensionsCm"]!["length"] = 0; break;
            case "TotalWeightOverLimit": firstLine["weightKg"] = 23_940.5m; break;
            case "ShipperReferenceTooLong": booking["shipperReference"] = $"{booking["shipperReference"]}-{new string('X', 100)}"; break;
            default: throw new ArgumentOutOfRangeException(nameof(code), code, "No way to break that rule.");
        }
    }

    private static string LaterBy(JsonNode timestamp, int hours) =>
        DateTimeOffset.Parse(timestamp.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).AddHours(hours).ToString("O");

    private static string[] CodesOf(JsonNode body) => [.. body["reasons"]!.AsArray().Select(r => r!["code"]!.GetValue<string>())];

    private static string NewReference() => $"BD-{Guid.NewGuid():N}"[..20];

    private static string NewCorrelationId() => Guid.NewGuid().ToString();

    private async Task<HttpResponseMessage> SubmitAsync(JsonObject booking, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        request.Headers.Add("X-Intake-Channel", "ShipperApi");
        return await tms.Tms.SendAsync(request, _cancellationToken);
    }

    private async Task<JsonArray> ListAsync(string status)
    {
        using var response = await tms.Tms.GetAsync(new Uri($"/v1/transport-orders?status={status}", UriKind.Relative), _cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadJsonAsync(response)).AsArray();
    }

    private async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;

    // Waits for the first event carrying the correlation ID, then lets the outbox run a few more cycles so a
    // duplicate would show up too.
    private async Task<IReadOnlyList<ServiceBusReceivedMessage>> PublishedEventsAsync(string correlationId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        timeout.CancelAfter(EventTimeout);
        while (!tms.Published.Any(m => m.CorrelationId == correlationId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }

        await Task.Delay(OutboxSettleTime, _cancellationToken);
        return [.. tms.Published.Where(m => m.CorrelationId == correlationId)];
    }
}
