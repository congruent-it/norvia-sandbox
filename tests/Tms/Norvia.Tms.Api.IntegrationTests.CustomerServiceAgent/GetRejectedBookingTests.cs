using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// <c>get_rejected_booking</c> through the stand-in to the TMS API's agent view, with a Rejected booking each test submits itself: the rejection codes and the
/// content the Shipper submitted, and nothing of the Consignee. The view is an allowlist, so a field it doesn't name is not in the answer.
/// </summary>
public sealed class GetRejectedBookingTests(CustomerServiceAgentFixture fixture)
{
    // Every property the agent view may carry (CustomerServiceAgentViews.cs); the answer's JSON names no other.
    private static readonly HashSet<string> Allowed =
    [
        "id", "status", "receivedAt", "correlationId", "channel", "shipper", "rejectionReasons", "submitted",
        "name", "code", "message", "field",
        "shipperReference", "collection", "delivery", "goodsLines",
        "postcode", "city", "country", "earliest", "latest",
        "packageType", "quantity", "weightKg", "dimensionsCm", "length", "width", "height",
    ];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARejectedBooking_IsReadWithItsRejectionCodesAndSubmittedContent()
    {
        var booking = await CustomerServiceAgentCalls.RejectAsync(fixture, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.GetRejectedBookingAsync(booking.Id, booking.CorrelationId, _cancellationToken);

        isError.Should().BeFalse(text);
        var view = JsonNode.Parse(text)!;
        view["id"]!.GetValue<Guid>().Should().Be(booking.Id);
        view["status"]!.GetValue<string>().Should().Be("Rejected");
        view["correlationId"]!.GetValue<string>().Should().Be(booking.CorrelationId);
        var reason = view["rejectionReasons"]!.AsArray().Should().ContainSingle().Subject!;
        reason["code"]!.GetValue<string>().Should().Be("WeightInvalid");
        reason["field"]!.GetValue<string>().Should().Be("goodsLines[].weightKg");
        view["submitted"]!["goodsLines"]!.AsArray().Select(line => line!["packageType"]!.GetValue<string>()).Should().Equal("pallet", "crate");
        view["submitted"]!["goodsLines"]![0]!["weightKg"]!.GetValue<decimal>().Should().Be(0);
        view["submitted"]!["collection"]!["city"]!.GetValue<string>().Should().Be("Rotterdam");
    }

    [Fact]
    public async Task ARejectedBooking_IsReadWithEveryConsigneeFieldRemoved()
    {
        var booking = await CustomerServiceAgentCalls.RejectAsync(fixture, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (isError, text) = await client.GetRejectedBookingAsync(booking.Id, booking.CorrelationId, _cancellationToken);

        isError.Should().BeFalse(text);
        text.Should().NotContainAny(CustomerServiceAgentCalls.NeverForTheAgent);
        var delivery = JsonNode.Parse(text)!["submitted"]!["delivery"]!.AsObject();
        delivery.Select(property => property.Key).Should().BeEquivalentTo(["earliest", "latest"], "the delivery has its window only");
    }

    [Fact]
    public async Task ARejectedBooking_IsReadWithNoFieldThatIsNotOnTheAllowlist()
    {
        var booking = await CustomerServiceAgentCalls.RejectAsync(fixture, _cancellationToken);
        using var client = fixture.CreateMcpClient(fixture.AgentToken());

        var (_, text) = await client.GetRejectedBookingAsync(booking.Id, booking.CorrelationId, _cancellationToken);

        PropertyNames(JsonNode.Parse(text)).Should().BeSubsetOf(Allowed);
        PropertyNames(JsonNode.Parse(text)).Should().NotContain(["consignee", "instructions", "street", "address", "transportOrder"]);
    }

    private static IEnumerable<string> PropertyNames(JsonNode? node) => node switch
    {
        JsonObject properties => properties.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
        JsonArray items => items.SelectMany(PropertyNames),
        _ => [],
    };
}
