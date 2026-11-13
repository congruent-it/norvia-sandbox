using System.Net;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using static Norvia.Tms.TestSupport.ProofsOfDelivery.ProofOfDeliveryCalls;

namespace Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;

/// <summary>
/// What a Proof of delivery tells the Planner (P6): the Shipments in progress mark one that has a Proof of delivery before
/// it is Delivered, and the Shipment's timeline shows each Proof of delivery, current and superseded, with its files.
/// </summary>
public sealed class ProofOfDeliveryPlannerQueryTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task InProgress_AShipmentWithAProofOfDeliveryBeforeItIsDelivered_IsMarked_AndOneWithoutIsNot()
    {
        var (withProof, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var (withoutProof, _) = await tms.BookedShipmentAsync(_cancellationToken);
        InProgress(await tms.ListShipmentsInProgressAsync(_cancellationToken), withProof)["hasProofOfDelivery"]!.GetValue<bool>().Should().BeFalse();

        await tms.RecordedProofAsync(Metadata(reference), [Cmr()], _cancellationToken);

        var listed = await tms.ListShipmentsInProgressAsync(_cancellationToken);
        InProgress(listed, withProof)["hasProofOfDelivery"]!.GetValue<bool>().Should().BeTrue();
        InProgress(listed, withProof)["status"]!.GetValue<string>().Should().Be("Booked");
        InProgress(listed, withoutProof)["hasProofOfDelivery"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Timeline_ShowsEachProofOfDelivery_CurrentAndSuperseded_WithItsDetailsAndFiles_AndReadsEachFile()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var blurry = Cmr("blurry.pdf");
        var clear = Cmr("clear.pdf");
        await tms.RecordedProofAsync(Metadata(reference, deliveryTime: DateTimeOffset.UtcNow.AddHours(-3)), [blurry], _cancellationToken);
        var clearer = Metadata(reference, deliveryTime: DateTimeOffset.UtcNow.AddHours(-2));
        clearer["receivedBy"] = "A. Schmidt";
        clearer["condition"] = "Reservations";
        clearer["remark"] = "One pallet damaged";
        await tms.RecordedProofAsync(clearer, [clear], _cancellationToken);

        var shipmentTimeline = await tms.GetTimelineAsync(shipmentNumber, _cancellationToken);
        shipmentTimeline["trackingEvents"]!.AsArray().Select(entry => entry!["type"]!.GetValue<string>()).Should().Equal("Delivered");
        var timeline = shipmentTimeline["proofsOfDelivery"]!.AsArray();

        timeline.Select(proof => proof!["isCurrent"]!.GetValue<bool>()).Should().Equal(false, true);
        var (superseded, current) = (timeline[0]!, timeline[1]!);
        superseded["receivedBy"]!.GetValue<string>().Should().Be("J. Becker");
        superseded["condition"]!.GetValue<string>().Should().Be("Clean");
        current["receivedBy"]!.GetValue<string>().Should().Be("A. Schmidt");
        current["condition"]!.GetValue<string>().Should().Be("Reservations");
        current["remark"]!.GetValue<string>().Should().Be("One pallet damaged");
        current["deliveryTime"].Should().NotBeNull();
        foreach (var (proof, sent) in new[] { (superseded, blurry), (current, clear) })
        {
            var file = proof["files"]!.AsArray().Should().ContainSingle().Subject!;
            file["name"]!.GetValue<string>().Should().Be(sent.Name);
            using var content = await tms.GetProofFileAsync(shipmentNumber, file["id"]!.GetValue<string>(), _cancellationToken);
            content.StatusCode.Should().Be(HttpStatusCode.OK);
            (await content.Content.ReadAsByteArrayAsync(_cancellationToken)).Should().Equal(sent.Content);
        }
    }

    [Fact]
    public async Task ReadingAFile_WhoseBlobTheRetentionPeriodDeleted_IsGoneWithTheExpiredCode_WhileTheRecordAndItsOtherFilesStay()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var expiring = Cmr("expiring.pdf");
        var kept = Cmr("kept.pdf");
        await tms.RecordedProofAsync(Metadata(reference), [expiring, kept], _cancellationToken);
        var files = (await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken)).Single()!["files"]!.AsArray();
        var expiringId = files.Single(file => file!["name"]!.GetValue<string>() == expiring.Name)!["id"]!.GetValue<string>();
        var keptId = files.Single(file => file!["name"]!.GetValue<string>() == kept.Name)!["id"]!.GetValue<string>();

        // The lifecycle rule of an Azure storage account does not run on Azurite: delete the blob as the rule would, by its name.
        var container = new BlobServiceClient(await tms.ConnectionStringAsync("blobs", _cancellationToken)).GetBlobContainerClient("proofs-of-delivery");
        (await container.DeleteBlobIfExistsAsync($"{shipmentNumber}/{Guid.Parse(expiringId):N}", cancellationToken: _cancellationToken)).Value
            .Should().BeTrue("the file was stored under the Shipment number and its ID");

        using var gone = await tms.GetProofFileAsync(shipmentNumber, expiringId, _cancellationToken);
        gone.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await JsonNode.ParseAsync(await gone.Content.ReadAsStreamAsync(_cancellationToken), cancellationToken: _cancellationToken))!["code"]!
            .GetValue<string>().Should().Be("ProofOfDeliveryFileExpired");
        using var stillThere = await tms.GetProofFileAsync(shipmentNumber, keptId, _cancellationToken);
        stillThere.StatusCode.Should().Be(HttpStatusCode.OK);
        (await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken)).Single()!["files"]!.AsArray()
            .Select(file => file!["name"]!.GetValue<string>()).Should().BeEquivalentTo(expiring.Name, kept.Name);
    }

    [Fact]
    public async Task Timeline_AShipmentWithNoProofOfDelivery_IsEmpty_AndAnUnknownShipmentIsNotFound()
    {
        var (shipmentNumber, _) = await tms.BookedShipmentAsync(_cancellationToken);

        (await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken)).Should().BeEmpty();
        using var unknown = await tms.Tms.GetAsync(new Uri("/v1/shipments/SH-9999999/tracking-events", UriKind.Relative), _cancellationToken);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReadingAFile_NeedsATmsToken()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        await tms.RecordedProofAsync(Metadata(reference), [Cmr()], _cancellationToken);
        var fileId = (await tms.GetProofsOnTimelineAsync(shipmentNumber, _cancellationToken))[0]!["files"]![0]!["id"]!.GetValue<string>();

        using var anonymous = new HttpClient { BaseAddress = tms.Tms.BaseAddress };
        using var response = await anonymous.GetAsync(new Uri($"/v1/shipments/{shipmentNumber}/proof-of-delivery/files/{fileId}", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static JsonNode InProgress(JsonArray listed, string shipmentNumber) =>
        listed.Single(entry => entry!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)!;
}
