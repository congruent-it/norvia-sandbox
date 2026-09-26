using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Drives TMS intake the way the intake Logic App does, and checks only what an outside observer can see: the HTTP
/// response, the query API and the events on the Transport Order events topic.
/// </summary>
public sealed class TransportOrderIntakeTests(TmsFixture tms)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OutboxSettleTime = TimeSpan.FromSeconds(3);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_ValidTransportOrder_IsAcceptedWithATransportOrderNumber()
    {
        using var response = await SubmitAsync(Bookings.Valid(NewReference()), NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadJsonAsync(response);
        body["transportOrderNumber"]!.GetValue<string>().Should().MatchRegex("^TO-[0-9]{7}$");
        body["status"]!.GetValue<string>().Should().Be("Accepted");
        body["repeat"]!.GetValue<bool>().Should().BeFalse();
        response.Headers.Location!.ToString().Should().EndWith($"/v1/transport-orders/{body["transportOrderNumber"]}");
    }

    [Fact]
    public async Task Submit_ValidTransportOrder_CanBeReadBackByNumber()
    {
        var booking = Bookings.Valid(NewReference());
        var correlationId = NewCorrelationId();
        var number = await SubmitAcceptedAsync(booking, correlationId);

        using var response = await tms.Tms.GetAsync(new Uri($"/v1/transport-orders/{number}", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await ReadJsonAsync(response);
        order["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        order["status"]!.GetValue<string>().Should().Be("Accepted");
        order["shipper"]!["id"]!.GetValue<string>().Should().Be("SHP-0001");
        order["shipper"]!["name"]!.GetValue<string>().Should().Be("Brouwer Dranken", "the TMS loads its Shippers from the seed file");
        order["channel"]!.GetValue<string>().Should().Be("ShipperApi");
        order["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        ShouldBeTheSubmittedTransportOrder(order["transportOrder"]!, booking, number);
    }

    [Fact]
    public async Task Submit_ValidTransportOrder_IsListedWithTheAcceptedTransportOrders()
    {
        var reference = NewReference();
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference), NewCorrelationId());

        using var response = await tms.Tms.GetAsync(new Uri("/v1/transport-orders?status=Accepted", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = (await ReadJsonAsync(response)).AsArray()
            .Single(o => o!["transportOrderNumber"]!.GetValue<string>() == number)!;
        listed["status"]!.GetValue<string>().Should().Be("Accepted");
        listed["shipperReference"]!.GetValue<string>().Should().Be(reference);
        listed["shipper"]!["name"]!.GetValue<string>().Should().Be("Brouwer Dranken");
    }

    [Fact]
    public async Task Submit_ValidTransportOrder_PublishesExactlyOneTransportOrderAccepted()
    {
        var booking = Bookings.Valid(NewReference());
        var correlationId = NewCorrelationId();
        var number = await SubmitAcceptedAsync(booking, correlationId);

        var events = await PublishedEventsAsync(correlationId);

        // Rating follows on the same Message trail with an event of its own.
        var message = events.Should().ContainSingle(m => m.Subject == "TransportOrderAccepted").Subject;
        message.CorrelationId.Should().Be(correlationId);
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["transportOrderNumber"] = number,
            ["flow"] = "transport-order-intake",
            ["partyId"] = "SHP-0001",
            ["eventType"] = "TransportOrderAccepted",
        });

        var body = JsonNode.Parse(message.Body.ToString())!;
        var validation = CanonicalModelSchemas.ValidateTransportOrderAccepted(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        body["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        ShouldBeTheSubmittedTransportOrder(body["transportOrder"]!, booking, number);
    }

    [Fact]
    public async Task Submit_WithoutCorrelationId_IsABadRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(Bookings.Valid(NewReference())),
        };

        using var response = await tms.Tms.SendAsync(request, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Submit_ValidTransportOrder_TracesCarryTheTrackingContractKeys()
    {
        var correlationId = NewCorrelationId();
        var number = await SubmitAcceptedAsync(Bookings.Valid(NewReference()), correlationId);

        var outcome = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["transportOrderNumber"] == number, _cancellationToken);

        outcome["flow"].Should().Be("transport-order-intake");
        outcome["partyId"].Should().Be("SHP-0001");
    }

    [Fact]
    public async Task Submit_ValidTransportOrder_PublishingTheEventJoinsTheSameTrail()
    {
        var correlationId = NewCorrelationId();
        var number = await SubmitAcceptedAsync(Bookings.Valid(NewReference()), correlationId);

        var published = await tms.TmsLogAsync(log => log["correlationId"] == correlationId && log["eventType"] == "TransportOrderAccepted", _cancellationToken);

        published["transportOrderNumber"].Should().Be(number);
        published["flow"].Should().Be("transport-order-intake");
        published["partyId"].Should().Be("SHP-0001");
    }

    [Fact]
    public async Task Submit_ValidTransportOrder_LogsNoConsigneeDetails()
    {
        var booking = Bookings.Valid(NewReference());
        var correlationId = NewCorrelationId();
        var number = await SubmitAcceptedAsync(booking, correlationId);
        await tms.TmsLogAsync(log => log["transportOrderNumber"] == number, _cancellationToken);

        var delivery = booking["delivery"]!;
        string[] personalData =
        [
            delivery["consignee"]!["name"]!.GetValue<string>(),
            delivery["consignee"]!["contactName"]!.GetValue<string>(),
            delivery["consignee"]!["phone"]!.GetValue<string>(),
            delivery["address"]!["street"]!.GetValue<string>(),
        ];
        tms.TmsLogs.Where(log => log["correlationId"] == correlationId).Should().NotBeEmpty()
            .And.NotContain(log => personalData.Any(value => log.Text.Contains(value, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Submit_FieldOutsideTheCanonicalModel_IsABadRequest()
    {
        var booking = Bookings.Valid(NewReference());
        booking["pickupAddress"] = "Waalhaven Oostzijde 88, Rotterdam";

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Submit_WithoutShipperId_IsABadRequest()
    {
        var booking = Bookings.Valid(NewReference());
        booking.Remove("shipperId");

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_WithATransportOrderNumber_IsABadRequest()
    {
        var booking = Bookings.Valid(NewReference());
        booking["transportOrderNumber"] = "TO-0000001";

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the TMS assigns Transport Order numbers");
    }

    [Fact]
    public async Task Submit_CountryNotAnIsoCode_IsABadRequest()
    {
        var booking = Bookings.Valid(NewReference());
        booking["delivery"]!["address"]!["country"] = "Netherlands";

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_EmptyGoodsLine_IsABadRequest()
    {
        var booking = Bookings.Valid(NewReference());
        booking["goodsLines"]!.AsArray().Add(null);

        using var response = await SubmitAsync(booking, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_UnknownNumber_IsNotFound()
    {
        using var response = await tms.Tms.GetAsync(new Uri("/v1/transport-orders/TO-9999999", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string NewReference() => $"BD-{Guid.NewGuid():N}"[..20];

    private static string NewCorrelationId() => Guid.NewGuid().ToString();

    private static void ShouldBeTheSubmittedTransportOrder(JsonNode actual, JsonObject submitted, string number)
    {
        var expected = submitted.DeepClone().AsObject();
        expected.Insert(0, "transportOrderNumber", number);
        JsonNode.DeepEquals(Normalize(actual), Normalize(expected)).Should().BeTrue(
            $"expected {expected.ToJsonString()} but found {actual.ToJsonString()}");
    }

    // Timestamps and numbers may be written differently (offset notation, 60 vs 60.0) without changing their value.
    private static JsonNode Normalize(JsonNode node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Normalize(p.Value)))),
        JsonArray a => new JsonArray([.. a.Select(i => i is null ? null : Normalize(i))]),
        JsonValue v when v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, out var t) && s.Contains('T', StringComparison.Ordinal) =>
            JsonValue.Create(t.UtcDateTime.ToString("O")),
        JsonValue v when v.TryGetValue<decimal>(out var d) => JsonValue.Create(d / 1.000000000000000000000000000000000m),
        _ => node.DeepClone(),
    };

    private async Task<HttpResponseMessage> SubmitAsync(JsonObject booking, string correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", correlationId);
        return await tms.Tms.SendAsync(request, _cancellationToken);
    }

    private async Task<string> SubmitAcceptedAsync(JsonObject booking, string correlationId)
    {
        using var response = await SubmitAsync(booking, correlationId);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(_cancellationToken));
        return (await ReadJsonAsync(response))["transportOrderNumber"]!.GetValue<string>();
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
