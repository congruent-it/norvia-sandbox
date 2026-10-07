using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Exceptions;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.ShipperTracking.ShipperTrackingCalls;

namespace Norvia.Tms.Api.IntegrationTests.ShipperTracking;

/// <summary>
/// A Shipper gets one Transport Order's tracking by number, or finds it by its own reference, with the same answer (P8). The
/// answer shows only progress, and nothing of the Carrier, the Shipment number, Exceptions, Stale or remarks.
/// </summary>
public sealed class ShipperTrackingGetTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_AnAcceptedTransportOrder_IsReceived_WithItsPlacesWindowsAndEta_AndMatchesTheSchema()
    {
        var reference = TmsCalls.NewReference();
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(reference), TmsCalls.NewCorrelationId(), _cancellationToken);

        var detail = await tms.GotAsync(Shipper, number, _cancellationToken);

        detail["trackingStatus"]!.GetValue<string>().Should().Be("Received");
        detail["shipperReference"]!.GetValue<string>().Should().Be(reference);
        detail["collection"]!["city"]!.GetValue<string>().Should().Be("Rotterdam");
        detail["delivery"]!["city"]!.GetValue<string>().Should().Be("Utrecht");
        detail["collectionWindow"]!["earliest"].Should().NotBeNull();
        detail["deliveryWindow"]!["latest"]!.GetValue<DateTimeOffset>().Should().Be(DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16));
        detail["eta"]!.GetValue<DateTimeOffset>().Should().Be(DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16));
        detail["lastUpdate"].Should().BeNull();
        detail["progress"]!.AsArray().Should().BeEmpty();
        var results = CanonicalModelSchemas.ValidateShipperTransportOrderDetail(detail);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
    }

    [Fact]
    public async Task Get_AndFind_ReturnTheSameAnswer_OnceTheShipmentIsUnderway()
    {
        var (shipmentNumber, carrierReference) = await tms.BookedShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var shipperReference = (await tms.ListedAsync(Shipper, number, _cancellationToken))["shipperReference"]!.GetValue<string>();
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await tms.RecordedAsync(TrackingEventCalls.Collected(carrierReference, now.AddHours(-4)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Departed", carrierReference, now.AddHours(-3), eta: now.AddHours(5)), _cancellationToken);

        var got = await tms.GotAsync(Shipper, number, _cancellationToken);
        using var response = await tms.FindAsync(tms.ShipperTracking, Shipper, shipperReference, _cancellationToken);
        var found = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        found.ToJsonString().Should().Be(got.ToJsonString());
        got["trackingStatus"]!.GetValue<string>().Should().Be("InTransit");
        got["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-3));
        got["eta"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(5));
        got["progress"]!.AsArray().Select(entry => entry!["type"]!.GetValue<string>()).Should().Equal("Collected", "Departed");
        got["progress"]![0]!["place"]!["city"]!.GetValue<string>().Should().Be("Rotterdam");
        got["progress"]![0]!["place"]!["countryCode"]!.GetValue<string>().Should().Be("NL");
        got["progress"]![0]!["eventTime"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-4));
        var results = CanonicalModelSchemas.ValidateShipperTransportOrderDetail(got);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
    }

    [Fact]
    public async Task Get_ADeliveredShipmentWithALateDeliveryFailed_StaysDelivered_AndShowsNoExceptionRemarkCarrierOrShipment()
    {
        var (shipmentNumber, carrierReference) = await tms.BookedShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await tms.RecordedAsync(TrackingEventCalls.Collected(carrierReference, now.AddHours(-4)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", carrierReference, now.AddHours(-2)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("DeliveryFailed", carrierReference, now.AddHours(-1)), _cancellationToken);

        var body = await tms.GotBodyAsync(Shipper, number, _cancellationToken);

        var detail = JsonNode.Parse(body)!;
        detail["trackingStatus"]!.GetValue<string>().Should().Be("Delivered");
        detail["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-2));
        detail["progress"]!.AsArray().Select(entry => entry!["type"]!.GetValue<string>()).Should().Equal("Collected", "Delivered");
        body.Should().NotContainEquivalentOf(shipmentNumber).And.NotContainEquivalentOf("CAR-").And.NotContainEquivalentOf("carrier")
            .And.NotContainEquivalentOf("exception").And.NotContainEquivalentOf("stale").And.NotContainEquivalentOf("shipment")
            .And.NotContainEquivalentOf("remark").And.NotContainEquivalentOf("dock 4").And.NotContainEquivalentOf("failed");
    }

    [Fact]
    public async Task Get_AnAgreedDeliveryWindow_ReplacesTheOrdersWindowAndTheFallbackEta()
    {
        var (shipmentNumber, exceptionNumber) = await tms.CollectedWithDeliveryFailedAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var earliest = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds());
        using var agreed = await tms.AgreeDeliveryWindowAsync(exceptionNumber, ExceptionCalls.Window(earliest, earliest.AddHours(4)), _cancellationToken);
        agreed.IsSuccessStatusCode.Should().BeTrue();

        var detail = await tms.GotAsync(Shipper, number, _cancellationToken);

        detail["deliveryWindow"]!["earliest"]!.GetValue<DateTimeOffset>().Should().Be(earliest);
        detail["deliveryWindow"]!["latest"]!.GetValue<DateTimeOffset>().Should().Be(earliest.AddHours(4));
        detail["eta"]!.GetValue<DateTimeOffset>().Should().Be(earliest.AddHours(4));
    }

    [Fact]
    public async Task Get_AndFind_AnotherShippersTransportOrder_IsNotFound_TheSameAsOneThatDoesNotExist()
    {
        var reference = TmsCalls.NewReference();
        var booking = Bookings.Valid(reference);
        booking["shipperId"] = AnotherShipper;
        var anothers = await tms.SubmitAcceptedAsync(booking, TmsCalls.NewCorrelationId(), _cancellationToken);

        using var getAnothers = await tms.GetAsync(tms.ShipperTracking, Shipper, anothers, _cancellationToken);
        using var getMissing = await tms.GetAsync(tms.ShipperTracking, Shipper, "TO-9999999", _cancellationToken);
        using var findAnothers = await tms.FindAsync(tms.ShipperTracking, Shipper, reference, _cancellationToken);
        using var findMissing = await tms.FindAsync(tms.ShipperTracking, Shipper, "no-such-reference", _cancellationToken);

        foreach (var response in new[] { getAnothers, getMissing, findAnothers, findMissing })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;
            problem["code"]!.GetValue<string>().Should().Be("TransportOrderNotFound");
        }

        (await getAnothers.Content.ReadAsStringAsync(_cancellationToken)).Should().NotContain(anothers).And.NotContain(AnotherShipper);
    }

    [Fact]
    public async Task Get_AndFind_WithoutAShipperId_OrAReference_AreABadRequest()
    {
        using var get = await tms.GetAsync(tms.ShipperTracking, shipperId: null, "TO-0000001", _cancellationToken);
        using var find = await tms.FindAsync(tms.ShipperTracking, Shipper, " ", _cancellationToken);

        get.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        find.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_AndFind_WithoutTheShipperTrackingRole_AreForbidden()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ManageRole]));

        using var get = await tms.GetAsync(caller, Shipper, "TO-0000001", _cancellationToken);
        using var find = await tms.FindAsync(caller, Shipper, "BD-1", _cancellationToken);

        get.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        find.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
