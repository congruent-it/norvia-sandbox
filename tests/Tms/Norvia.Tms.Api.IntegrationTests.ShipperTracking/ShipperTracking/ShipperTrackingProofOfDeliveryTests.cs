using System.Net;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.ProofsOfDelivery.ProofOfDeliveryCalls;
using static Norvia.Tms.TestSupport.ShipperTracking.ShipperTrackingCalls;

namespace Norvia.Tms.Api.IntegrationTests.ShipperTracking;

/// <summary>
/// A Shipper sees the current Proof of delivery of its Transport Order and reads its files, and nothing of a superseded one (P8).
/// </summary>
public sealed class ShipperTrackingProofOfDeliveryTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Detail_ShowsTheCurrentProofOfDeliveryWithItsFiles_AndNoSupersededOne_AndMatchesTheSchema()
    {
        var (number, superseded, clear) = await SupersededThenCurrentAsync();

        var body = await tms.GotBodyAsync(Shipper, number, _cancellationToken);

        var detail = JsonNode.Parse(body)!;
        var proof = detail["proofOfDelivery"]!;
        proof["receivedBy"]!.GetValue<string>().Should().Be("A. Schmidt");
        proof["condition"]!.GetValue<string>().Should().Be("Reservations");
        proof["deliveryTime"].Should().NotBeNull();
        proof["files"]!.AsArray().Should().ContainSingle().Subject!["name"]!.GetValue<string>().Should().Be(clear.Name);
        body.Should().NotContain("blurry").And.NotContain(superseded);
        var results = CanonicalModelSchemas.ValidateShipperTransportOrderDetail(detail);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
    }

    [Fact]
    public async Task Detail_WithNoProofOfDelivery_ShowsNone()
    {
        var (shipmentNumber, _) = await tms.BookedShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);

        (await tms.GotAsync(Shipper, number, _cancellationToken))["proofOfDelivery"].Should().BeNull();
    }

    [Fact]
    public async Task ReadingAFile_OfTheCurrentProofOfDelivery_StreamsIt()
    {
        var (number, _, clear) = await SupersededThenCurrentAsync();
        var fileId = (await tms.GotAsync(Shipper, number, _cancellationToken))["proofOfDelivery"]!["files"]![0]!["id"]!.GetValue<string>();

        using var read = await tms.GetProofFileAsync(tms.ShipperTracking, Shipper, number, fileId, _cancellationToken);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        read.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await read.Content.ReadAsByteArrayAsync(_cancellationToken)).Should().Equal(clear.Content);
    }

    [Fact]
    public async Task ReadingAFile_OfASupersededProofOfDelivery_IsProofOfDeliveryNotFound()
    {
        var (number, superseded, _) = await SupersededThenCurrentAsync();

        using var response = await tms.GetProofFileAsync(tms.ShipperTracking, Shipper, number, superseded, _cancellationToken);

        await AssertNotFoundAsync(response, "ProofOfDeliveryNotFound");
    }

    [Fact]
    public async Task ReadingAFile_OfATransportOrderWithNoProofOfDelivery_IsProofOfDeliveryNotFound()
    {
        var (shipmentNumber, _) = await tms.BookedShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);

        using var response = await tms.GetProofFileAsync(tms.ShipperTracking, Shipper, number, Guid.NewGuid().ToString(), _cancellationToken);

        await AssertNotFoundAsync(response, "ProofOfDeliveryNotFound");
    }

    [Fact]
    public async Task ReadingAFile_OfAnotherShippersTransportOrder_IsTransportOrderNotFound()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        await tms.RecordedProofAsync(Metadata(reference), [Cmr()], _cancellationToken);
        var fileId = (await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken)).Single()!["files"]![0]!["id"]!.GetValue<string>();

        using var response = await tms.GetProofFileAsync(tms.ShipperTracking, AnotherShipper, number, fileId, _cancellationToken);

        await AssertNotFoundAsync(response, "TransportOrderNotFound");
    }

    [Fact]
    public async Task ReadingAFile_WithoutAShipperId_IsABadRequest_AndWithoutTheShipperTrackingRole_IsForbidden()
    {
        using var caller = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ManageRole]));

        using var noShipper = await tms.GetProofFileAsync(tms.ShipperTracking, shipperId: null, "TO-0000001", Guid.NewGuid().ToString(), _cancellationToken);
        using var forbidden = await tms.GetProofFileAsync(caller, Shipper, "TO-0000001", Guid.NewGuid().ToString(), _cancellationToken);

        noShipper.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>A delivered Shipment whose first Proof of delivery (blurry.pdf) a clearer one (clear.pdf) superseded.</summary>
    private async Task<(string Number, string SupersededFileId, SentFile Current)> SupersededThenCurrentAsync()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var number = await tms.TransportOrderOfAsync(shipmentNumber, _cancellationToken);
        var clear = Cmr("clear.pdf");
        await tms.RecordedProofAsync(Metadata(reference, deliveryTime: DateTimeOffset.UtcNow.AddHours(-3)), [Cmr("blurry.pdf")], _cancellationToken);
        var superseded = (await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken)).Single()!["files"]![0]!["id"]!.GetValue<string>();
        var clearer = Metadata(reference, deliveryTime: DateTimeOffset.UtcNow.AddHours(-2));
        clearer["receivedBy"] = "A. Schmidt";
        clearer["condition"] = "Reservations";
        await tms.RecordedProofAsync(clearer, [clear], _cancellationToken);
        return (number, superseded, clear);
    }

    private async Task AssertNotFoundAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["code"]!.GetValue<string>().Should().Be(code);
    }
}
