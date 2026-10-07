using System.Text.Json;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

public sealed class ShipperTrackingFormatTests
{
    private static ShipperTrackingDetail Detail(string sample) =>
        ShipperTrackingFormat.ReadDetail(Contracts.Read($"PartnerApi/samples/shipper-tracking/{sample}.shipper-tracking.json"));

    [Fact]
    public void ReadDetail_ADeliveredTransportOrder_ReadsEverythingTheShipperSees()
    {
        var order = Detail("delivered-winter");

        order.TransportOrderNumber.Should().Be("TO-0000008");
        order.ShipperReference.Should().Be("BD-2030-0008");
        order.Status.Should().Be(ShipperTrackingStatus.Delivered);
        order.Pickup.Should().Be(new ShipperPlace("Hamburg", "DE"));
        order.Receiver.Should().Be(new ShipperPlace("London", "GB"));
        (order.PickupFrom, order.PickupUntil, order.DeliverFrom, order.DeliverUntil, order.Eta, order.LastUpdate)
            .Should().Be(("09-12-2030 08:00", "09-12-2030 12:00", "11-12-2030 09:00", "11-12-2030 13:00", "11-12-2030 10:30", "11-12-2030 10:05"));
    }

    [Fact]
    public void ReadDetail_KeepsTheTimesAsTheApiReturnsThem_AndTheProgressInItsOrder()
    {
        var progress = Detail("delivered-winter").Progress;

        progress.Select(step => (step.Type, step.Time, step.Place.ToString())).Should().Equal(
            (ShipperProgressType.Collected, "09-12-2030 09:30", "Hamburg, DE"),
            (ShipperProgressType.Departed, "09-12-2030 10:15", "Hamburg, DE"),
            (ShipperProgressType.Arrived, "10-12-2030 14:20", "Dover, GB"),
            (ShipperProgressType.OutForDelivery, "11-12-2030 08:00", "London, GB"),
            (ShipperProgressType.Delivered, "11-12-2030 10:05", "London, GB"));
    }

    [Fact]
    public void ReadDetail_TheCurrentProofOfDelivery_HasItsFilesToDownload()
    {
        var proof = Detail("delivered-winter").ProofOfDelivery!;

        proof.ReceivedBy.Should().Be("M. \"Max\" Müller");
        proof.DeliveredAt.Should().Be("11-12-2030 10:05");
        proof.Condition.Should().Be(ProofOfDeliveryCondition.Reservations);
        proof.Files.Select(file => (file.FileId, file.Name, file.ContentType, file.SizeBytes)).Should().Equal(
            (Guid.Parse("6f1d3b52-8a4e-4c9d-b7e1-2a5c9e0f4d11"), "CMR \\ signed.pdf", "application/pdf", 48213L),
            (Guid.Parse("a2c4e6f8-1b3d-4f5a-9c7e-0d2b4f6a8c22"), "damage-photo.jpg", "image/jpeg", 1523004L));
    }

    [Fact]
    public void ReadDetail_ATransportOrderJustReceived_HasNoProgressNoProofAndNoTimesAfterTheWindows()
    {
        var order = Detail("received-minimal");

        order.Status.Should().Be(ShipperTrackingStatus.Received);
        order.Progress.Should().BeEmpty();
        order.ProofOfDelivery.Should().BeNull();
        order.LastUpdate.Should().BeNull();
    }

    [Theory]
    [InlineData("clocks-back-london")]
    [InlineData("clocks-forward-finland")]
    [InlineData("in-transit-summer")]
    [InlineData("year-end-sofia")]
    public void ReadDetail_EverySample_IsRead(string sample) => Detail(sample).TransportOrderNumber.Should().StartWith("TO-");

    [Fact]
    public void ReadList_TheMixedStatuses_ReadsEachItem()
    {
        var list = ShipperTrackingFormat.ReadList(Contracts.Read("PartnerApi/samples/shipper-tracking-lists/mixed-statuses.shipper-tracking-list.json"));

        list.Select(item => item.Status).Should().Equal(
            ShipperTrackingStatus.Received, ShipperTrackingStatus.Scheduled, ShipperTrackingStatus.Collected, ShipperTrackingStatus.InTransit, ShipperTrackingStatus.Delivered);
        list[0].ShipperReference.Should().Be("BD-2030-0001");
        list[0].Eta.Should().Be("02-07-2030 13:00");
        list[1].ShipperReference.Should().BeNull();
        list[2].LastUpdate.Should().Be("01-07-2030 09:10");
        list[3].Receiver.Should().Be(new ShipperPlace("London", "GB"));
    }

    [Fact]
    public void ReadList_AnEmptyList_IsEmpty() =>
        ShipperTrackingFormat.ReadList(Contracts.Read("PartnerApi/samples/shipper-tracking-lists/empty.shipper-tracking-list.json")).Should().BeEmpty();

    [Theory]
    [InlineData("""{ "norviaOrderNo": "TO-0000001", "status": "LOST", "events": [] }""")]
    [InlineData("""{ "norviaOrderNo": "TO-0000001", "status": "RECEIVED", "events": [ { "code": "EXPLODED", "time": "01-01-2030 10:00" } ] }""")]
    [InlineData("""{ "norviaOrderNo": "TO-0000001", "status": "RECEIVED", "events": [], "proofOfDelivery": { "receivedBy": "A", "deliveredAt": "01-01-2030 10:00", "condition": "WET", "files": [] } }""")]
    [InlineData("""{ "status": "RECEIVED", "events": [] }""")]
    [InlineData("""{ "norviaOrderNo": "TO-0000001", "status": "RECEIVED" }""")]
    [InlineData("""[]""")]
    public void ReadDetail_AnAnswerBreakingTheFormat_Throws(string body) =>
        ((Action)(() => ShipperTrackingFormat.ReadDetail(body))).Should().Throw<JsonException>();

    [Fact]
    public void ReadList_ANonArray_Throws() =>
        ((Action)(() => ShipperTrackingFormat.ReadList("{}"))).Should().Throw<JsonException>();

    [Fact]
    public void QueryOf_NoFilter_AsksForTheFirstPage() =>
        ShipperTrackingFormat.QueryOf(new ShipperTrackingFilter()).Should().Be("page=1&pageSize=25");

    [Fact]
    public void QueryOf_AFullFilter_NamesTheCanonicalStatus_AndDatesAsIso() =>
        ShipperTrackingFormat.QueryOf(new ShipperTrackingFilter(ShipperTrackingStatus.InTransit, new DateOnly(2030, 7, 1), new DateOnly(2030, 7, 31), Page: 3))
            .Should().Be("trackingStatus=InTransit&collectionFrom=2030-07-01&collectionTo=2030-07-31&page=3&pageSize=25");

    [Theory]
    [InlineData(ShipperTrackingStatus.Received, "Received")]
    [InlineData(ShipperTrackingStatus.Scheduled, "Scheduled")]
    [InlineData(ShipperTrackingStatus.Collected, "Collected")]
    [InlineData(ShipperTrackingStatus.Delivered, "Delivered")]
    public void QueryOf_AStatus_IsTheCanonicalName(ShipperTrackingStatus status, string name) =>
        ShipperTrackingFormat.QueryOf(new ShipperTrackingFilter(status)).Should().StartWith($"trackingStatus={name}&");

    [Fact]
    public void QueryOf_AStatusItDoesNotKnow_Throws() =>
        ((Action)(() => ShipperTrackingFormat.QueryOf(new ShipperTrackingFilter((ShipperTrackingStatus)99)))).Should().Throw<ArgumentOutOfRangeException>();
}
