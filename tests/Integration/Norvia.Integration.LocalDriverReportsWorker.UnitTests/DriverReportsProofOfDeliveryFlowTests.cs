using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Norvia.Integration.LocalDriverReportsWorker.UnitTests;

/// <summary>
/// The Driver reports Logic App's steps for a Proof of delivery, played locally (ADR 0009, ADR 0016): fetch the files from the Driver app's transit container, make
/// P6's multipart call to the TMS as Own fleet, settle the message, tell the Driver app, and delete the files once the TMS accepted the Proof of delivery.
/// </summary>
public sealed partial class DriverReportsProofOfDeliveryFlowTests
{
    private static readonly Lazy<JsonSchema> ProofSchema = new(() => JsonSchema.FromText(ReadResource("CanonicalModel/proof-of-delivery.v1.schema.json")));
    private static readonly Lazy<JsonSchema> CapturedSchema = new(() => JsonSchema.FromText(ReadResource("CanonicalModel/proof-of-delivery-captured.v1.schema.json")));

    private readonly DriverReportsWorld _world = new();

    private static JsonObject MetadataOf(StubHttp.Request call) => JsonNode.Parse(MetadataPart().Match(call.Body).Groups["json"].Value)!.AsObject();

    [GeneratedRegex(@"name=metadata\r?\n\r?\n(?<json>\{.*?\})\r?\n--", RegexOptions.Singleline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MetadataPart();

    [Fact]
    public async Task AProofOfDelivery_IsPostedToTheProofOfDeliveryApi_AsMultipart_AsOwnFleet_WithTheDriverIdFromTheEvent_TheDriversCorrelationId_AndTheRecordRole()
    {
        var outcome = await _world.RunAsync(DriverReportsWorld.ProofCaptured());

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        var call = _world.Tms.Requests.Should().ContainSingle().Subject;
        call.Uri.ToString().Should().Be("http://tms.test/v1/proofs-of-delivery");
        call.Headers["X-Correlation-ID"].Should().Be(DriverReportsWorld.ProofCorrelationId, "the Delivered Tracking event travels under the same one");
        DriverReportsWorld.RoleOf(call).Should().Be("ProofsOfDelivery.Record");
        call.Body.Should().Contain("name=files; filename=signature.png").And.Contain("name=files; filename=photo-1.jpg").And.Contain("Content-Type: image/png").And.Contain("Content-Type: image/jpeg");
        call.Body.IndexOf("filename=signature.png", StringComparison.Ordinal).Should().BeLessThan(call.Body.IndexOf("filename=photo-1.jpg", StringComparison.Ordinal));
        var metadata = MetadataOf(call);
        metadata["documentId"]!.GetValue<string>().Should().Be(DriverReportsWorld.ProofEventId);
        metadata["source"]!["type"]!.GetValue<string>().Should().Be("OwnFleet");
        metadata["source"]!["driverId"]!.GetValue<string>().Should().Be("DRV-0002");
        metadata["tripNumber"]!.GetValue<string>().Should().Be("TR-0000012");
        metadata["shipmentNumber"]!.GetValue<string>().Should().Be("SH-0000031");
        metadata["tripVersion"]!.GetValue<int>().Should().Be(3);
        metadata["receivedBy"]!.GetValue<string>().Should().Be("J. Bakker");
        metadata["deliveryTime"]!.GetValue<string>().Should().Be("2030-10-03T10:12:30+02:00");
        metadata["location"]!["place"]!.GetValue<string>().Should().Be("Utrecht");
        metadata["condition"]!.GetValue<string>().Should().Be("Clean");
        metadata.ContainsKey("remark").Should().BeFalse();
        metadata["files"]!.AsArray().Select(file => file!["name"]!.GetValue<string>()).Should().Equal("signature.png", "photo-1.jpg");
        metadata["files"]![0]!.AsObject().ContainsKey("blobReference").Should().BeFalse("the metadata describes the files parts and carries no reference");
    }

    [Fact]
    public async Task TheMetadata_ValidatesAgainstTheCanonicalModel_WithReservationsToo()
    {
        foreach (var (condition, remark) in new[] { ("Clean", (string?)null), ("Reservations", "Two cartons crushed") })
        {
            var world = new DriverReportsWorld();
            await world.RunAsync(DriverReportsWorld.ProofCaptured(condition, remark));

            var metadata = MetadataOf(world.Tms.Requests.Single());
            ProofSchema.Value.Evaluate(JsonSerializer.SerializeToElement(metadata), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true })
                .IsValid.Should().BeTrue(metadata.ToJsonString());
            metadata["remark"]?.GetValue<string>().Should().Be(remark);
        }
    }

    [Fact]
    public async Task ARecordedProofOfDelivery_CompletesTheMessage_ReportsRecorded_ThenDeletesItsFiles()
    {
        await _world.RunAsync(DriverReportsWorld.ProofCaptured());

        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(Settlement.Complete);
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("Recorded");
        reported["driverAppEventId"]!.GetValue<string>().Should().Be(DriverReportsWorld.ProofEventId);
        reported["correlationId"]!.GetValue<string>().Should().Be(DriverReportsWorld.ProofCorrelationId);
        _world.ProofFiles.Deleted.Should().Equal(DriverReportsWorld.ProofBlobs);
    }

    [Fact]
    public async Task ARefusedFile_LikeAHeic_IsDeadLetteredWithTheTmsCode_ReportedRefused_AndTheFilesStayForInvestigation()
    {
        _world.Tms.Otherwise(HttpStatusCode.UnprocessableEntity, DriverReportsWorld.Problem(422, "FileNotAccepted", "File photo-1.jpg isn't a PDF, JPEG or PNG."));

        var outcome = await _world.RunAsync(DriverReportsWorld.ProofCaptured());

        outcome.Should().Be(DriverReportsRunOutcome.Done);
        _world.Tms.Requests.Should().ContainSingle("a refusal won't change by sending it again");
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().Be(new Settlement.DeadLetter("FileNotAccepted", "File photo-1.jpg isn't a PDF, JPEG or PNG."));
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("Refused");
        reported["failureCode"]!.GetValue<string>().Should().Be("FileNotAccepted");
        _world.ProofFiles.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task ATmsThatStaysDown_IsRetriedWithTheSameFiles_ThenReportedNotRecorded_AndTheFilesStay()
    {
        _world.Tms.Otherwise(HttpStatusCode.ServiceUnavailable, "Service Unavailable");

        await _world.RunAsync(DriverReportsWorld.ProofCaptured());

        _world.Tms.Requests.Should().HaveCount(4);
        _world.Tms.Requests.Should().OnlyContain(request => request.Body.Contains("filename=signature.png", StringComparison.Ordinal));
        _world.OutcomeReported()["outcome"]!.GetValue<string>().Should().Be("NotRecorded");
        _world.ProofFiles.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task FilesThatAreGone_FailTheRun_DeadLetterTheMessage_ReportNotRecorded_AndSendNothingToTheTms()
    {
        _world.ProofFiles.Held.Remove(DriverReportsWorld.ProofBlobs[1]);

        var outcome = await _world.RunAsync(DriverReportsWorld.ProofCaptured());

        outcome.Should().Be(DriverReportsRunOutcome.FilesGone);
        _world.Tms.Requests.Should().BeEmpty();
        _world.Settled.Should().ContainSingle().Which.Settlement.Should().BeOfType<Settlement.DeadLetter>().Which.Reason.Should().Be("ProofOfDeliveryFilesGone");
        var reported = _world.OutcomeReported();
        reported["outcome"]!.GetValue<string>().Should().Be("NotRecorded");
        reported["failureCode"]!.GetValue<string>().Should().Be("ProofOfDeliveryFilesGone");
        reported["driverAppEventId"]!.GetValue<string>().Should().Be(DriverReportsWorld.ProofEventId);
        _world.ProofFiles.Deleted.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000e2", "eventType": "ProofOfDeliveryCaptured", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002" }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000e2", "eventType": "ProofOfDeliveryCaptured", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "version": 1, "shipmentNumber": "SH-0000031", "documentId": "d", "receivedBy": "x", "deliveryTime": "2030-10-03T10:12:30+02:00", "condition": "Clean", "location": { "place": "a", "postcode": "b", "countryCode": "NL" }, "files": [] }""")]
    [InlineData("""{ "eventId": "0198e7c0-0000-7000-8000-0000000000e2", "eventType": "ProofOfDeliveryCaptured", "correlationId": "c", "tripNumber": "TR-0000012", "driverId": "DRV-0002", "version": 1, "shipmentNumber": "SH-0000031", "documentId": "d", "receivedBy": "x", "deliveryTime": "2030-10-03T10:12:30+02:00", "condition": "Clean", "location": { "place": "a", "postcode": "b", "countryCode": "NL" }, "files": [ { "name": "a.png" } ] }""")]
    public async Task AProofOfDeliveryThatIsMissingWhatTheTmsNeeds_IsDeadLetteredAsUnreadable(string message)
    {
        var outcome = await _world.RunAsync(message);

        outcome.Should().Be(DriverReportsRunOutcome.Unreadable);
        _world.Tms.Requests.Should().BeEmpty();
    }

    [Fact]
    public void TheEventTheDriverAppPublishes_IsTheOneTheFlowReads()
    {
        foreach (var message in new[] { DriverReportsWorld.ProofCaptured(), DriverReportsWorld.ProofCaptured("Reservations", "Crushed") })
        {
            CapturedSchema.Value.Evaluate(JsonSerializer.SerializeToElement(JsonNode.Parse(message)), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true })
                .IsValid.Should().BeTrue(message);
        }
    }

    [Fact]
    public async Task TheSamples_AreReadByTheFlow_AndTheirFilesAreTheOnesItFetches()
    {
        foreach (var sample in typeof(DriverReportsProofOfDeliveryFlowTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("CanonicalModel/samples/proof-of-delivery-captured/", StringComparison.Ordinal)))
        {
            var world = new DriverReportsWorld();
            var sent = JsonNode.Parse(ReadResource(sample))!;
            foreach (var file in sent["files"]!.AsArray())
            {
                world.ProofFiles.Held[file!["blobReference"]!.GetValue<string>()] = [1];
            }

            var outcome = await world.RunAsync(sent.ToJsonString());

            outcome.Should().Be(DriverReportsRunOutcome.Done, sample);
            ProofSchema.Value.Evaluate(JsonSerializer.SerializeToElement(MetadataOf(world.Tms.Requests.Single())), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true })
                .IsValid.Should().BeTrue(sample);
            world.ProofFiles.Deleted.Should().HaveCount(sent["files"]!.AsArray().Count);
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(DriverReportsProofOfDeliveryFlowTests).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
