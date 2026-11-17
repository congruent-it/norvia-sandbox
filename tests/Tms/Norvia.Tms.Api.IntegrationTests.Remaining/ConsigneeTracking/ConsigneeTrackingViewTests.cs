using System.Net;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.ConsigneeTracking.ConsigneeTrackingCalls;
using static Norvia.Tms.TestSupport.ProofsOfDelivery.ProofOfDeliveryCalls;

namespace Norvia.Tms.Api.IntegrationTests.ConsigneeTracking;

/// <summary>
/// A tracking code opens the Consignee's view of one Transport Order, which is the Shipper's progress with what only the Shipper
/// or the Planner may know taken out, whatever state the Shipment is in (ADR 0052, ADR 0011, P8).
/// </summary>
public sealed class ConsigneeTrackingViewTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private static readonly string[] ViewKeys =
        ["transportOrderNumber", "collection", "delivery", "deliveryWindow", "trackingStatus", "eta", "lastUpdate", "progress", "proofOfDelivery"];

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task View_AValidCode_ReturnsTheProjection_WithTheKeysOfTheConsigneeViewAndNoMore()
    {
        var (number, code) = await tms.AcceptedWithCodeAsync(_cancellationToken);

        var view = await tms.ViewedAsync(code, _cancellationToken);

        view.AsObject().Select(pair => pair.Key).Should().BeEquivalentTo(ViewKeys);
        view["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
        view["trackingStatus"]!.GetValue<string>().Should().Be("Received");
        view["collection"]!["city"]!.GetValue<string>().Should().Be("Rotterdam");
        view["delivery"]!["city"]!.GetValue<string>().Should().Be("Utrecht");
        view["deliveryWindow"]!["latest"]!.GetValue<DateTimeOffset>().Should().Be(DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16));
        view["eta"]!.GetValue<DateTimeOffset>().Should().Be(DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16));
        view["lastUpdate"].Should().BeNull();
        view["progress"]!.AsArray().Should().BeEmpty();
        view["proofOfDelivery"].Should().BeNull();
    }

    [Fact]
    public async Task View_ACodeTypedInLowerCaseWithLookAlikes_OpensTheSameTransportOrder()
    {
        var (number, code) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        var typed = code.ToLowerInvariant().Replace('1', 'l').Replace('0', 'o');

        var view = await tms.ViewedAsync(typed, _cancellationToken);

        view["transportOrderNumber"]!.GetValue<string>().Should().Be(number);
    }

    [Fact]
    public async Task View_TheShippersAndTheConsigneesView_AgreeOnProgress_ButTheConsigneeSeesNoShipperReference()
    {
        var (_, reference, number, code) = await tms.BookedWithCodeAsync(_cancellationToken);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await tms.RecordedAsync(TrackingEventCalls.Collected(reference, now.AddHours(-4)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Departed", reference, now.AddHours(-3), eta: now.AddHours(5)), _cancellationToken);

        var shippers = await tms.GotAsync(ShipperTrackingCalls.Shipper, number, _cancellationToken);
        var body = await tms.ViewBodyAsync(code, _cancellationToken);

        var consignees = JsonNode.Parse(body)!;
        consignees["trackingStatus"]!.GetValue<string>().Should().Be("InTransit");
        consignees["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-3));
        consignees["eta"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(5));
        consignees["progress"]!.ToJsonString().Should().Be(shippers["progress"]!.ToJsonString());
        consignees["deliveryWindow"]!.ToJsonString().Should().Be(shippers["deliveryWindow"]!.ToJsonString());
        body.Should().NotContain(shippers["shipperReference"]!.GetValue<string>());
        consignees.AsObject().ContainsKey("shipperReference").Should().BeFalse();
    }

    [Fact]
    public async Task View_ADeliveredShipmentWithAnExceptionAndANote_NeverShowsTheCarrierShipmentNumberExceptionRemarkOrPrice()
    {
        var (shipmentNumber, exceptionNumber) = await tms.CollectedWithDeliveryFailedAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var code = await tms.TrackingCodeOfAsync(number, _cancellationToken);
        using var noted = await tms.AddNoteAsync(exceptionNumber, ExceptionCalls.Note("Consignee closed, dock 4 locked, price disputed"), _cancellationToken);
        noted.IsSuccessStatusCode.Should().BeTrue();

        var body = await tms.ViewBodyAsync(code, _cancellationToken);

        AssertNothingOnlyTheShipperMayKnow(body, shipmentNumber);
        JsonNode.Parse(body)!["trackingStatus"]!.GetValue<string>().Should().Be("InTransit");
    }

    [Fact]
    public async Task View_ADeliveredShipmentWithAProofOfDelivery_NeverShowsTheCarrierShipmentNumberExceptionRemarkOrPrice()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var code = await tms.TrackingCodeOfAsync(number, _cancellationToken);
        await tms.RecordedProofAsync(Metadata(reference), [Cmr()], _cancellationToken);

        var body = await tms.ViewBodyAsync(code, _cancellationToken);

        AssertNothingOnlyTheShipperMayKnow(body, shipmentNumber);
        body.Should().NotContainEquivalentOf("signed at the dock");
    }

    [Fact]
    public async Task View_TheProofOfDeliverySummaryShows_ButItsFilesAreNotOffered()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var code = await tms.TrackingCodeOfAsync(number, _cancellationToken);
        var file = Cmr("secret-cmr.pdf");
        await tms.RecordedProofAsync(Metadata(reference, condition: "Reservations"), [file], _cancellationToken);
        var fileId = FileIdOf((await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken)).Single()!, file.Name);

        var body = await tms.ViewBodyAsync(code, _cancellationToken);
        using var read = await ShipperTrackingCalls.GetProofFileAsync(tms, tms.ConsigneeTracking, ShipperTrackingCalls.Shipper, number, fileId, _cancellationToken);

        var proof = JsonNode.Parse(body)!["proofOfDelivery"]!.AsObject();
        proof["receivedBy"]!.GetValue<string>().Should().Be("J. Becker");
        proof["condition"]!.GetValue<string>().Should().Be("Reservations");
        proof["deliveryTime"].Should().NotBeNull();
        proof.ContainsKey("files").Should().BeFalse();
        body.Should().NotContain(file.Name).And.NotContain(fileId);
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task View_AShipmentSentBackToPlanned_StaysScheduled()
    {
        var (carrierBookingNumber, shipmentNumber) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var code = await tms.TrackingCodeOfAsync(number, _cancellationToken);
        (await tms.ViewedAsync(code, _cancellationToken))["trackingStatus"]!.GetValue<string>().Should().Be("Scheduled");

        await tms.RejectedAsync(carrierBookingNumber, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);

        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Planned");
        (await tms.ViewedAsync(code, _cancellationToken))["trackingStatus"]!.GetValue<string>().Should().Be("Scheduled");
    }

    [Fact]
    public async Task View_ADeliveredShipmentWithALateDeliveryFailed_StaysDelivered_AtTheTimeItWasDelivered()
    {
        var (_, reference, _, code) = await tms.BookedWithCodeAsync(_cancellationToken);
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await tms.RecordedAsync(TrackingEventCalls.Collected(reference, now.AddHours(-4)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, now.AddHours(-2)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("DeliveryFailed", reference, now.AddHours(-1)), _cancellationToken);

        var view = await tms.ViewedAsync(code, _cancellationToken);

        view["trackingStatus"]!.GetValue<string>().Should().Be("Delivered");
        view["lastUpdate"]!.GetValue<DateTimeOffset>().Should().Be(now.AddHours(-2));
        view["progress"]!.AsArray().Select(entry => entry!["type"]!.GetValue<string>()).Should().Equal("Collected", "Delivered");
    }

    [Fact]
    public async Task View_AnAgreedDeliveryWindow_ReplacesTheOrdersWindowAndTheFallbackEta()
    {
        var (shipmentNumber, exceptionNumber) = await tms.CollectedWithDeliveryFailedAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var code = await tms.TrackingCodeOfAsync(number, _cancellationToken);
        var earliest = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds());
        using var agreed = await tms.AgreeDeliveryWindowAsync(exceptionNumber, ExceptionCalls.Window(earliest, earliest.AddHours(4)), _cancellationToken);
        agreed.IsSuccessStatusCode.Should().BeTrue();

        var view = await tms.ViewedAsync(code, _cancellationToken);

        view["deliveryWindow"]!["earliest"]!.GetValue<DateTimeOffset>().Should().Be(earliest);
        view["deliveryWindow"]!["latest"]!.GetValue<DateTimeOffset>().Should().Be(earliest.AddHours(4));
    }

    private static void AssertNothingOnlyTheShipperMayKnow(string body, string shipmentNumber) =>
        body.Should().NotContainEquivalentOf(shipmentNumber).And.NotContainEquivalentOf("CAR-").And.NotContainEquivalentOf("carrier")
            .And.NotContainEquivalentOf("exception").And.NotContainEquivalentOf("stale").And.NotContainEquivalentOf("shipment")
            .And.NotContainEquivalentOf("remark").And.NotContainEquivalentOf("note").And.NotContainEquivalentOf("dock 4")
            .And.NotContainEquivalentOf("price").And.NotContainEquivalentOf("amount").And.NotContainEquivalentOf("eur")
            .And.NotContainEquivalentOf("shipperReference").And.NotContainEquivalentOf("shipperId");
}
