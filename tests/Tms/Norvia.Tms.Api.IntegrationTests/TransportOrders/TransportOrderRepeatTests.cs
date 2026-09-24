using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;

namespace Norvia.Tms.Api.IntegrationTests.TransportOrders;

/// <summary>
/// Sends bookings to TMS intake more than once, the way a double click or a Logic App retry does, and checks only
/// what an outside observer can see: the HTTP response, the query API and the events on the Transport Order events topic.
/// </summary>
public sealed class TransportOrderRepeatTests(TmsFixture tms)
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OutboxSettleTime = TimeSpan.FromSeconds(3);
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_SameBookingAgain_ReturnsTheOriginalTransportOrderFlaggedAsARepeat()
    {
        var booking = Bookings.Valid(NewReference());
        using var first = await SubmitAsync(booking, NewCorrelationId());
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var original = await ReadJsonAsync(first);

        using var again = await SubmitAsync(booking, NewCorrelationId());

        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync(_cancellationToken));
        var repeat = await ReadJsonAsync(again);
        repeat["transportOrderNumber"]!.GetValue<string>().Should().Be(original["transportOrderNumber"]!.GetValue<string>());
        repeat["status"]!.GetValue<string>().Should().Be("Accepted");
        repeat["repeat"]!.GetValue<bool>().Should().BeTrue();
        original["repeat"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Submit_SameBookingAgain_PublishesNoEvent()
    {
        var booking = Bookings.Valid(NewReference());
        var firstCorrelationId = NewCorrelationId();
        var number = await SubmitAcceptedAsync(booking, firstCorrelationId);
        (await PublishedEventsAsync(firstCorrelationId)).Should().ContainSingle();

        var againCorrelationId = NewCorrelationId();
        using var again = await SubmitAsync(booking, againCorrelationId);
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(OutboxSettleTime, _cancellationToken);
        tms.Published.Should().NotContain(m => m.CorrelationId == againCorrelationId);
        tms.Published.Where(m => Equals(m.ApplicationProperties.GetValueOrDefault("transportOrderNumber"), number))
            .Should().ContainSingle("the original acceptance is the only event for this Transport Order");
    }

    [Fact]
    public async Task Submit_SameBookingAgain_BooksNoSecondTransportOrder()
    {
        var reference = NewReference();
        var booking = Bookings.Valid(reference);
        await SubmitAcceptedAsync(booking, NewCorrelationId());

        using var again = await SubmitAsync(booking, NewCorrelationId());
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListAsync("Accepted")).Where(o => o!["shipperReference"]!.GetValue<string>() == reference).Should().ContainSingle();
        (await ListAsync("Rejected")).Where(o => o!["shipperReference"]?.GetValue<string>() == reference).Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_SameBookingManyTimesAtOnce_BooksOneTransportOrderAndAnswersTheRestAsRepeats()
    {
        var reference = NewReference();
        var booking = Bookings.Valid(reference);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SubmitAsync(booking, NewCorrelationId())));

        try
        {
            responses.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.Created);
            responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
            var numbers = await Task.WhenAll(responses.Select(async r => (await ReadJsonAsync(r))["transportOrderNumber"]!.GetValue<string>()));
            numbers.Distinct().Should().ContainSingle();
            (await ListAsync("Accepted")).Where(o => o!["shipperReference"]!.GetValue<string>() == reference).Should().ContainSingle();
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Submit_ReferenceReusedForDifferentContent_IsRejectedWithShipperReferenceReused()
    {
        var reference = NewReference();
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference), NewCorrelationId());
        var different = Bookings.Valid(reference);
        different["instructions"] = "Call ahead, the gate is locked after 15:00";

        using var response = await SubmitAsync(different, NewCorrelationId());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(_cancellationToken));
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var reason = (await ReadJsonAsync(response))["reasons"]!.AsArray().Should().ContainSingle().Subject!;
        reason["code"]!.GetValue<string>().Should().Be("ShipperReferenceReused");
        reason["message"]!.GetValue<string>().Should().Contain(reference).And.Contain(number);
    }

    [Fact]
    public async Task Submit_ReferenceReusedForDifferentContent_IsKeptAsARejectedBookingAndLeavesTheOriginalAlone()
    {
        var reference = NewReference();
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference), NewCorrelationId());
        var different = Bookings.Valid(reference);
        different["goodsLines"]![0]!["quantity"] = 3;
        var correlationId = NewCorrelationId();
        using var rejection = await SubmitAsync(different, correlationId);

        using var rejected = await tms.Tms.GetAsync(
            new Uri((await ReadJsonAsync(rejection))["instance"]!.GetValue<string>(), UriKind.Relative), _cancellationToken);
        using var original = await tms.Tms.GetAsync(new Uri($"/v1/transport-orders/{number}", UriKind.Relative), _cancellationToken);

        var rejectedBooking = await ReadJsonAsync(rejected);
        rejectedBooking["status"]!.GetValue<string>().Should().Be("Rejected");
        rejectedBooking["rejectionReasons"]![0]!["code"]!.GetValue<string>().Should().Be("ShipperReferenceReused");
        rejectedBooking["transportOrder"]!["goodsLines"]![0]!["quantity"]!.GetValue<int>().Should().Be(3);
        var originalOrder = await ReadJsonAsync(original);
        originalOrder["status"]!.GetValue<string>().Should().Be("Accepted");
        originalOrder["transportOrder"]!["goodsLines"]![0]!["quantity"]!.GetValue<int>().Should().Be(2);

        var message = (await PublishedEventsAsync(correlationId)).Should().ContainSingle().Subject;
        message.Subject.Should().Be("TransportOrderRejected");
    }

    [Fact]
    public async Task Submit_SameReferenceForTwoShippers_IsAcceptedForBoth()
    {
        var reference = NewReference();
        var otherShippers = Bookings.Valid(reference);
        otherShippers["shipperId"] = "SHP-0003";

        var first = await SubmitAcceptedAsync(Bookings.Valid(reference), NewCorrelationId());
        var second = await SubmitAcceptedAsync(otherShippers, NewCorrelationId());

        second.Should().NotBe(first, "each Shipper's booking is its own Transport Order");
    }

    [Fact]
    public async Task Submit_ReferenceOfARejectedBooking_CanBeUsedForTheCorrectedBooking()
    {
        var reference = NewReference();
        var broken = Bookings.Valid(reference);
        broken["goodsLines"] = new JsonArray();
        using var rejection = await SubmitAsync(broken, NewCorrelationId());
        rejection.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var corrected = await SubmitAsync(Bookings.Valid(reference), NewCorrelationId());

        corrected.StatusCode.Should().Be(HttpStatusCode.Created, "a Rejected booking holds no Shipper reference");
    }

    private static string NewReference() => $"BD-{Guid.NewGuid():N}"[..20];

    private static string NewCorrelationId() => Guid.NewGuid().ToString();

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
