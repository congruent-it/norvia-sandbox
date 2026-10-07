using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>The sample pairs in contracts/partner-api/samples/shipper-tracking and shipper-tracking-lists: the TMS's Canonical Model answer, and the Shipper tracking format the Liquid map makes of it.</summary>
public sealed class ShipperTrackingMapSampleTests
{
    private const string DetailFolder = "PartnerApi/samples/shipper-tracking";
    private const string ListFolder = "PartnerApi/samples/shipper-tracking-lists";

    public static TheoryData<string> DetailSamples => Samples(DetailFolder, ".shipper-transport-order-detail.json");

    public static TheoryData<string> ListSamples => Samples(ListFolder, ".shipper-transport-order-list-items.json");

    [Theory]
    [MemberData(nameof(DetailSamples))]
    public void Detail_Sample_GivesTheSampleShipperTracking(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{DetailFolder}/{sample}.shipper-tracking.json"));

        var tracking = ShipperTrackingMaps.Detail(Contracts.Read($"{DetailFolder}/{sample}.shipper-transport-order-detail.json"));

        JsonNode.DeepEquals(tracking, expected).Should().BeTrue($"the map gave {tracking.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(DetailSamples))]
    public void Detail_Sample_KeepsToTheShipperTrackingSchema(string sample)
    {
        var tracking = ShipperTrackingMaps.Detail(Contracts.Read($"{DetailFolder}/{sample}.shipper-transport-order-detail.json"));

        var results = Schemas.ShipperTracking.Validate(tracking);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(DetailSamples))]
    public void Detail_SampleCanonicalAnswer_KeepsToTheCanonicalSchema(string sample)
    {
        var results = Schemas.ShipperTransportOrderDetail.Validate(
            JsonNode.Parse(Contracts.Read($"{DetailFolder}/{sample}.shipper-transport-order-detail.json")));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(ListSamples))]
    public void List_Sample_GivesTheSampleShipperTrackingList(string sample)
    {
        var expected = JsonNode.Parse(Contracts.Read($"{ListFolder}/{sample}.shipper-tracking-list.json"));

        var list = ShipperTrackingMaps.List(Contracts.Read($"{ListFolder}/{sample}.shipper-transport-order-list-items.json"));

        JsonNode.DeepEquals(list, expected).Should().BeTrue($"the map gave {list.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(ListSamples))]
    public void List_Sample_EveryItemKeepsToTheShipperTrackingListItemSchema(string sample)
    {
        var list = ShipperTrackingMaps.List(Contracts.Read($"{ListFolder}/{sample}.shipper-transport-order-list-items.json")).AsArray();

        foreach (var item in list)
        {
            var results = Schemas.ShipperTrackingListItem.Validate(item);
            results.IsValid.Should().BeTrue(Schemas.Errors(results));
        }
    }

    [Theory]
    [MemberData(nameof(ListSamples))]
    public void List_SampleCanonicalAnswer_EveryItemKeepsToTheCanonicalSchema(string sample)
    {
        var items = JsonNode.Parse(Contracts.Read($"{ListFolder}/{sample}.shipper-transport-order-list-items.json"))!.AsArray();

        foreach (var item in items)
        {
            var results = Schemas.ShipperTransportOrderListItem.Validate(item);
            results.IsValid.Should().BeTrue(Schemas.Errors(results));
        }
    }

    [Fact]
    public void Detail_SampleWithUnknownTrackingStatus_BreaksTheShipperTrackingSchema()
    {
        var canonical = JsonNode.Parse(Contracts.Read($"{DetailFolder}/in-transit-summer.shipper-transport-order-detail.json"))!.AsObject();
        canonical["trackingStatus"] = "Lost";

        var tracking = ShipperTrackingMaps.Detail(canonical.ToJsonString());

        Schemas.ShipperTracking.Validate(tracking).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Detail_SampleWithUnknownEventType_BreaksTheShipperTrackingSchema()
    {
        var canonical = JsonNode.Parse(Contracts.Read($"{DetailFolder}/in-transit-summer.shipper-transport-order-detail.json"))!.AsObject();
        canonical["progress"]!.AsArray()[0]!["type"] = "Teleported";

        var tracking = ShipperTrackingMaps.Detail(canonical.ToJsonString());

        Schemas.ShipperTracking.Validate(tracking).IsValid.Should().BeFalse();
    }

    private static TheoryData<string> Samples(string folder, string suffix) =>
        new(Contracts.FileNames(folder).Where(name => name.EndsWith(suffix, StringComparison.Ordinal)).Select(name => name[..^suffix.Length]));
}
