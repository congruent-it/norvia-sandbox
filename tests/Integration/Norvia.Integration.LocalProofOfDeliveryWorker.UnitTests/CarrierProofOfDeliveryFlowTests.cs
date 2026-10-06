using System.Net;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.LocalProofOfDeliveryWorker.UnitTests;

/// <summary>The local worker settles a Proof of delivery message as the Carrier Proof of delivery workflow does (ADR 0032).</summary>
public class CarrierProofOfDeliveryFlowTests
{
    private const string Recorded = """{"shipmentNumber":"SH-0000042","shipmentStatus":"Delivered","documentId":"VDT-POD-2001","isRepeat":false}""";

    [Fact]
    public async Task RunAsync_TheTmsRecordsTheProof_DeletesTheBlob_AndCompletesTheMessage()
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.Then(HttpStatusCode.OK, Recorded);

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Should().Be(new ProofOfDeliveryRun(ProofOfDeliverySettlement.Complete));
        run.RunFailed.Should().BeFalse();
        world.Inbound.Deleted.Should().Equal(ProofOfDeliveryWorld.BlobName);
    }

    [Fact]
    public async Task RunAsync_MakesOneMultipartCall_ForTheCarrierOfTheMessage_NotThePayload()
    {
        var world = new ProofOfDeliveryWorld();
        var proof = JsonNode.Parse(ProofOfDeliveryWorld.Clean)!.AsObject();
        proof["carrierId"] = "CAR-0003";
        world.Inbound.Blobs[ProofOfDeliveryWorld.BlobName] = proof.ToJsonString();

        await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        var call = world.Tms.Requests.Should().ContainSingle().Subject;
        call.Uri.AbsolutePath.Should().Be("/v1/proofs-of-delivery");
        call.Body.Should().Contain("name=metadata").And.Contain("name=files; filename=cmr.pdf").And.Contain("\"carrierId\":\"CAR-0001\"");
        call.Body.Should().NotContain("CAR-0003").And.Contain("%PDF-1.4");
    }

    [Fact]
    public async Task RunAsync_CallsTheTmsUnderTheMessagesCorrelationId_AsTheRecordRoleIdentity()
    {
        var world = new ProofOfDeliveryWorld();

        await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(correlationId: "c-77"), CancellationToken.None);

        var call = world.Tms.Requests.Single();
        call.Headers["X-Correlation-ID"].Should().Be("c-77");
        var token = new JsonWebTokenHandler().ReadJsonWebToken(call.Headers["Authorization"]["Bearer ".Length..]);
        token.GetClaim("roles").Value.Should().Be("ProofsOfDelivery.Record");
        token.GetClaim("azp").Value.Should().Be("proof-of-delivery-worker");
    }

    [Fact]
    public async Task RunAsync_AClientError_DeadLettersWithTheFailureCodeAsReasonAndTheMessageAsDescription_WithoutRetry_KeepingTheBlob()
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.Otherwise(HttpStatusCode.NotFound, """{"code":"ShipmentNotFound","detail":"No Confirmed Carrier booking has that reference."}""");

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Should().Be(new ProofOfDeliveryRun(ProofOfDeliverySettlement.DeadLetter, "ShipmentNotFound", "No Confirmed Carrier booking has that reference."));
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().ContainSingle();
        world.Inbound.Deleted.Should().BeEmpty();
        world.Inbound.Blobs.Should().ContainKey(ProofOfDeliveryWorld.BlobName);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RunAsync_AServerErrorOrTimeout_AbandonsTheMessage_ForTheQueueToDeliverAgain_KeepingTheBlob(HttpStatusCode status)
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.Otherwise(status);

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Should().Be(new ProofOfDeliveryRun(ProofOfDeliverySettlement.Retry));
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().ContainSingle("the retries come from the queue, not from the call");
        world.Inbound.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TheTmsDoesNotAnswer_AbandonsTheMessage()
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.ThenFail(new HttpRequestException("connection refused"));

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Settlement.Should().Be(ProofOfDeliverySettlement.Retry);
        world.KeysOf("Retrying").Should().Contain("failureCode", "TmsUnavailable");
    }

    [Fact]
    public async Task RunAsync_AMissingBlob_DeadLettersTheMessage_AndFailsTheRun_WithoutCallingTheTms()
    {
        var world = new ProofOfDeliveryWorld();
        world.Inbound.Blobs.Clear();

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Should().Be(new ProofOfDeliveryRun(ProofOfDeliverySettlement.DeadLetter, "ProofOfDeliveryBlobMissing", run.DeadLetterDescription));
        run.DeadLetterDescription.Should().NotBeNullOrWhiteSpace();
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().BeEmpty();
        world.KeysOf("DeadLettered").Should().Contain("failureCode", "ProofOfDeliveryBlobMissing");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"container":"inbound-proofs-of-delivery"}""")]
    public async Task RunAsync_AReferenceWithoutABlobName_IsAMissingBlob(string reference)
    {
        var world = new ProofOfDeliveryWorld();

        var run = await world.Flow.RunAsync(reference, ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.DeadLetterReason.Should().Be("ProofOfDeliveryBlobMissing");
        world.Tms.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"bookingRef":"B-1"}""")]
    public async Task RunAsync_InvalidContent_DeadLettersTheMessageAsUnmappable_AndFailsTheRun_KeepingTheBlob(string content)
    {
        var world = new ProofOfDeliveryWorld();
        world.Inbound.Blobs[ProofOfDeliveryWorld.BlobName] = content;

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Should().Be(new ProofOfDeliveryRun(ProofOfDeliverySettlement.DeadLetter, "UnmappableProofOfDelivery", run.DeadLetterDescription));
        run.RunFailed.Should().BeTrue();
        world.Tms.Requests.Should().BeEmpty();
        world.Inbound.Blobs.Should().ContainKey(ProofOfDeliveryWorld.BlobName);
        world.KeysOf("DeadLettered").Should().Contain("failureCode", "UnmappableProofOfDelivery");
    }

    [Fact]
    public async Task RunAsync_AMessageWithoutAPartyId_IsUnmappable_BecauseTheCarrierComesFromTheToken()
    {
        var world = new ProofOfDeliveryWorld();

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), new Dictionary<string, object> { ["correlationId"] = "c-1" }, CancellationToken.None);

        run.DeadLetterReason.Should().Be("UnmappableProofOfDelivery");
        world.Tms.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenTheBlobCannotBeDeletedAfterTheTmsAcceptedIt_AbandonsTheMessage_SoTheRepeatCompletesItLater()
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.Then(HttpStatusCode.OK, Recorded);
        world.Inbound.DeleteFailure = new InvalidOperationException("Storage is down");

        var run = await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        run.Should().Be(new ProofOfDeliveryRun(ProofOfDeliverySettlement.Retry));
        world.Logs.GetSnapshot().Should().Contain(log => log.Level == Microsoft.Extensions.Logging.LogLevel.Warning && log.Message.Contains("couldn't be deleted"));
        world.KeysOf("Succeeded").Should().Contain("shipmentNumber", "SH-0000042", "the TMS has the Proof of delivery");
    }

    [Fact]
    public async Task RunAsync_TracesWithTheTrackingContractsKeys_AddingTheShipmentAndBookingNumbersAndTheOutcome()
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.Then(HttpStatusCode.OK, Recorded);

        await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        world.KeysOf("Succeeded").Should()
            .Contain("correlationId", "c-1").And.Contain("flow", "carrier-proof-of-delivery").And.Contain("partyId", "CAR-0001")
            .And.Contain("sourceMessageId", "VDT-POD-2001").And.Contain("shipmentNumber", "SH-0000042").And.Contain("carrierBookingNumber", "CB-0000042");
        world.KeysOf("Succeeded").Should().NotContainKey("failureCode");
    }

    [Fact]
    public async Task RunAsync_ARefusal_TracesTheFailureCodeAsDeadLettered()
    {
        var world = new ProofOfDeliveryWorld();
        world.Tms.Otherwise(HttpStatusCode.BadRequest, """{"code":"FileNotAccepted","detail":"File cmr.pdf isn't a PDF, JPEG or PNG."}""");

        await world.Flow.RunAsync(ProofOfDeliveryWorld.Reference(), ProofOfDeliveryWorld.Properties(), CancellationToken.None);

        world.KeysOf("DeadLettered").Should().Contain("failureCode", "FileNotAccepted");
    }
}
