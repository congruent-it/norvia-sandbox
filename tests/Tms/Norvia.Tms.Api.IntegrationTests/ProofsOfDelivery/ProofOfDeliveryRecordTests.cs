using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.CarrierBookings;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery.ProofOfDeliveryCalls;

namespace Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;

/// <summary>
/// A Carrier's Proof of delivery is recorded through the TMS's Proof of delivery API, as the Carrier Proof of delivery
/// Logic App calls it (P6): one multipart call with the canonical metadata and a file. The TMS keeps the file in its own
/// blob storage (Azurite in the test host), records the Proof of delivery as the Shipment's current one, and publishes one
/// ProofOfDeliveryReceived. It never changes the Shipment's status.
/// </summary>
public sealed class ProofOfDeliveryRecordTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Record_ACleanProofOfDeliveryForADeliveredShipment_StoresItAsCurrent_KeepsItsFile_AndLeavesTheStatusAlone()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var cmr = Cmr();
        var correlationId = TmsCalls.NewCorrelationId();
        var deliveryTime = new DateTimeOffset(2026, 9, 25, 14, 30, 0, TimeSpan.FromHours(2));

        var recorded = await tms.RecordedProofAsync(Metadata(reference, documentId: "doc-1", deliveryTime: deliveryTime), [cmr], _cancellationToken, correlationId);

        recorded["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Delivered");
        recorded["documentId"]!.GetValue<string>().Should().Be("doc-1");
        recorded["numberOfFiles"]!.GetValue<int>().Should().Be(1);
        recorded["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Delivered");

        using var current = await tms.GetProofAsync(shipmentNumber, _cancellationToken);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        var proof = JsonNode.Parse(await current.Content.ReadAsStringAsync(_cancellationToken))!;
        proof["id"]!.GetValue<string>().Should().Be(recorded["proofOfDeliveryId"]!.GetValue<string>());
        proof["source"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        proof["receivedBy"]!.GetValue<string>().Should().Be("J. Becker");
        proof["condition"]!.GetValue<string>().Should().Be("Clean");
        proof["remark"]!.GetValue<string>().Should().Be("Signed at the dock");
        proof["place"]!.GetValue<string>().Should().Be("Düsseldorf");
        proof["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        DateTimeOffset.Parse(proof["deliveryTime"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().Be(deliveryTime);
        var file = proof["files"]!.AsArray().Should().ContainSingle().Subject!;
        file["name"]!.GetValue<string>().Should().Be("cmr.pdf");
        file["contentType"]!.GetValue<string>().Should().Be("application/pdf");
        file["sizeBytes"]!.GetValue<long>().Should().Be(cmr.Content.Length);

        using var content = await tms.GetProofFileAsync(shipmentNumber, file["id"]!.GetValue<string>(), _cancellationToken);
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await content.Content.ReadAsByteArrayAsync(_cancellationToken)).Should().Equal(cmr.Content);
    }

    [Fact]
    public async Task Record_PublishesExactlyOneProofOfDeliveryReceived_WithNoFileContentOrLink()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        var deliveryTime = new DateTimeOffset(2026, 9, 25, 14, 30, 0, TimeSpan.FromHours(2));

        await tms.RecordedProofAsync(Metadata(reference, deliveryTime: deliveryTime), [Cmr()], _cancellationToken, correlationId);

        var message = (await tms.PublishedAsync(correlationId, "ProofOfDeliveryReceived", _cancellationToken)).Should().ContainSingle().Subject;
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateProofOfDeliveryReceived(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().MatchRegex("^TO-[0-9]{7}$");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["carrierBookingReference"]!.GetValue<string>().Should().Be(reference);
        body["receivedBy"]!.GetValue<string>().Should().Be("J. Becker");
        var published = DateTimeOffset.Parse(body["deliveryTime"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        published.Should().Be(deliveryTime);
        published.Offset.Should().Be(TimeSpan.FromHours(2), "the delivery time keeps the offset of where it happened");
        body["condition"]!.GetValue<string>().Should().Be("Clean");
        body["remark"]!.GetValue<string>().Should().Be("Signed at the dock");
        body["numberOfFiles"]!.GetValue<int>().Should().Be(1);
        body["replacesEarlier"]!.GetValue<bool>().Should().BeFalse();
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        message.CorrelationId.Should().Be(correlationId);
        message.ApplicationProperties.Should().Contain("shipmentNumber", shipmentNumber);
        message.ApplicationProperties.Should().Contain("flow", "carrier-proof-of-delivery");
        message.Body.ToString().Should().NotContain("content").And.NotContain("http", "the event carries no file content and no link");
        tms.Published.Count(m => m.Subject == "ProofOfDeliveryReceived" && CarrierBookingCalls.NamesShipment(m, shipmentNumber)).Should().Be(1);
    }

    [Fact]
    public async Task Record_ForABookedShipment_AlsoLeavesItsStatusAlone()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);

        var recorded = await tms.RecordedProofAsync(Metadata(reference), [Cmr()], _cancellationToken);

        recorded["shipmentStatus"]!.GetValue<string>().Should().Be("Booked");
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Booked");
    }

    [Fact]
    public async Task Record_TracesTheProofOfDeliveryOnItsOwnTrail_WithTheCarriersDocumentId()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedProofAsync(Metadata(reference, documentId: "doc-trace"), [Cmr()], _cancellationToken, correlationId);

        var log = await tms.TmsLogAsync(l => l["correlationId"] == correlationId && l["outcome"] == "Succeeded", _cancellationToken);
        log["flow"].Should().Be("carrier-proof-of-delivery");
        log["partyId"].Should().Be(PlanningCalls.CarrierId);
        log["sourceMessageId"].Should().Be("doc-trace");
        log["shipmentNumber"].Should().Be(shipmentNumber);
    }

    [Fact]
    public async Task Record_AReferenceNoBookingOfTheCarrierHas_IsRefused_WithShipmentNotFound_AndPublishesNothing()
    {
        var correlationId = TmsCalls.NewCorrelationId();

        var problem = await tms.RecordProofRefusedAsync(Metadata("NO-SUCH-REFERENCE"), [Cmr()], HttpStatusCode.NotFound, _cancellationToken, correlationId);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
        problem["correlationId"]!.GetValue<string>().Should().Be(correlationId);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().BeEmpty();
    }

    [Fact]
    public async Task Record_WithOnlyTheCarrierBookingNumber_FindsTheShipmentByIt()
    {
        var (shipmentNumber, number, _) = await tms.BookedShipmentWithNumberAsync(_cancellationToken);
        var metadata = Metadata("DOES-NOT-MATCH");
        metadata["carrierBookingNumber"] = number;

        var recorded = await tms.RecordedProofAsync(metadata, [Cmr()], _cancellationToken);

        recorded["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
    }

    [Fact]
    public async Task Record_ByAnotherCarrierWithTheReferenceOfAShipmentNotItsOwn_IsRefused_WithShipmentNotFound()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);

        var problem = await tms.RecordProofRefusedAsync(
            Metadata(reference, source: TrackingEventCalls.CarrierSource(PlanningCalls.OtherCarrierId)), [Cmr()], HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
    }

    [Fact]
    public async Task Record_ForARejectedCarrierBooking_IsRefused_WithShipmentNotFound()
    {
        var (number, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        await tms.RejectedAsync(number, PlanningCalls.CarrierId, "NoCapacity", _cancellationToken);
        var metadata = Metadata("DOES-NOT-MATCH");
        metadata["carrierBookingNumber"] = number;

        var problem = await tms.RecordProofRefusedAsync(metadata, [Cmr()], HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound");
    }

    [Fact]
    public async Task Record_ForAReleasedShipmentWhoseBookingIsNotConfirmedYet_IsRefused_WithShipmentNotFound()
    {
        var (number, _) = await tms.ReleasedCarrierBookingAsync(_cancellationToken);
        var metadata = Metadata("DOES-NOT-MATCH");
        metadata["carrierBookingNumber"] = number;

        var problem = await tms.RecordProofRefusedAsync(metadata, [Cmr()], HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotFound", "a Released Shipment's booking isn't Confirmed yet, so the Carrier has no booking to find it by");
    }

    [Fact]
    public async Task Record_ADeliveryTimeInTheFuture_IsRefused_AndPublishesNothing()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        var problem = await tms.RecordProofRefusedAsync(
            Metadata(reference, deliveryTime: DateTimeOffset.UtcNow.AddHours(1)), [Cmr()], HttpStatusCode.BadRequest, _cancellationToken, correlationId);

        problem["code"]!.GetValue<string>().Should().Be("DeliveryTimeInTheFuture");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().BeEmpty();
    }

    [Fact]
    public async Task Record_ADeliveryTimeJustAheadOfTheClock_WithinTheSkew_IsAccepted()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);

        var deliveryTime = DateTimeOffset.UtcNow.AddMinutes(1);

        var recorded = await tms.RecordedProofAsync(Metadata(reference, deliveryTime: deliveryTime), [Cmr()], _cancellationToken);

        var proof = await CurrentProofAsync(recorded["shipmentNumber"]!.GetValue<string>());
        DateTimeOffset.Parse(proof["deliveryTime"]!.GetValue<string>(), CultureInfo.InvariantCulture).Should().BeCloseTo(deliveryTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Record_WithReservationsButNoRemark_IsRefused_WithMissingRequiredField()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var metadata = Metadata(reference);
        metadata["condition"] = "Reservations";
        metadata.Remove("remark");

        var problem = await tms.RecordProofRefusedAsync(metadata, [Cmr()], HttpStatusCode.BadRequest, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("MissingRequiredField");
    }

    [Fact]
    public async Task Record_WithReservationsAndARemark_IsAccepted()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var metadata = Metadata(reference);
        metadata["condition"] = "Reservations";
        metadata["remark"] = "Two pallets damaged";

        var recorded = await tms.RecordedProofAsync(metadata, [Cmr()], _cancellationToken);

        var proof = await CurrentProofAsync(recorded["shipmentNumber"]!.GetValue<string>());
        proof["condition"]!.GetValue<string>().Should().Be("Reservations");
        proof["remark"]!.GetValue<string>().Should().Be("Two pallets damaged");
    }

    private async Task<JsonNode> CurrentProofAsync(string shipmentNumber)
    {
        using var response = await tms.GetProofAsync(shipmentNumber, _cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;
    }

    [Fact]
    public async Task Record_WithoutAField_IsRefused_WithMissingRequiredField()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var metadata = Metadata(reference);
        metadata.Remove("receivedBy");

        var problem = await tms.RecordProofRefusedAsync(metadata, [Cmr()], HttpStatusCode.BadRequest, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("MissingRequiredField");
    }

    public static TheoryData<string, int> BadFileSets => new()
    {
        { "none", 0 },
        { "six", 6 },
        { "over 10 MB", 1 },
        { "heic named jpg", 1 },
    };

    [Theory]
    [MemberData(nameof(BadFileSets))]
    public async Task Record_WithFilesThatBreakTheFileRules_IsRefused_WithFileNotAccepted_AndStoresNothing(string kind, int count)
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var files = kind switch
        {
            "over 10 MB" => [new SentFile("big.pdf", "application/pdf", [.. "%PDF-"u8, .. new byte[(10 * 1024 * 1024) + 1]])],
            "heic named jpg" => [new SentFile("photo.jpg", "image/jpeg", [0, 0, 0, 0x18, .. "ftypheic"u8, .. new byte[64]])],
            _ => Enumerable.Range(0, count).Select(i => Cmr($"cmr-{i}.pdf")).ToList(),
        };

        var problem = await tms.RecordProofRefusedAsync(Metadata(reference), files, HttpStatusCode.BadRequest, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("FileNotAccepted");
        using var current = await tms.GetProofAsync(shipmentNumber, _cancellationToken);
        current.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Record_FiveFiles_AreAccepted()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);

        var recorded = await tms.RecordedProofAsync(Metadata(reference), [.. Enumerable.Range(0, 5).Select(i => Cmr($"cmr-{i}.pdf"))], _cancellationToken);

        recorded["numberOfFiles"]!.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public async Task Record_APngNamedJpgAndDeclaredAsJpeg_IsKeptAsThePngItIs()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[16]];

        await tms.RecordedProofAsync(Metadata(reference), [new SentFile("a.jpg", "image/jpeg", png)], _cancellationToken);

        var proof = await CurrentProofAsync(shipmentNumber);
        proof["files"]!.AsArray().Should().ContainSingle().Which!["contentType"]!.GetValue<string>().Should().Be("image/png");
    }

    [Fact]
    public async Task Record_ByACallerWithoutTheRecordRole_IsForbidden()
    {
        var (_, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);

        using var response = await tms.RecordProofAsync(tms.TrackingRecord, Metadata(reference), [Cmr()], _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Record_WithoutAToken_IsUnauthorized()
    {
        using var caller = tms.CreateTmsClient(token: null);

        using var response = await tms.RecordProofAsync(caller, Metadata("VDT-1"), [Cmr()], _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProof_ForAShipmentWithNone_IsNotFound()
    {
        var (shipmentNumber, _) = await tms.DeliveredShipmentAsync(_cancellationToken);

        using var response = await tms.GetProofAsync(shipmentNumber, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
