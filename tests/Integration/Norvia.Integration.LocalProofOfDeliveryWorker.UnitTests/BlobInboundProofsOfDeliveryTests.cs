using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Norvia.Integration.LocalProofOfDeliveryWorker.UnitTests;

public class BlobInboundProofsOfDeliveryTests
{
    private readonly BlobClient _blob = Substitute.For<BlobClient>();
    private readonly BlobInboundProofsOfDelivery _inbound;

    public BlobInboundProofsOfDeliveryTests()
    {
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient("c-1").Returns(_blob);
        var service = Substitute.For<BlobServiceClient>();
        service.GetBlobContainerClient("inbound-proofs-of-delivery").Returns(container);
        _inbound = new BlobInboundProofsOfDelivery(service, new ProofOfDeliveryWorld().Options, NullLogger<BlobInboundProofsOfDelivery>.Instance);
    }

    [Fact]
    public async Task ReadAsync_ReturnsTheBlobsContentAsText()
    {
        var result = BlobsModelFactory.BlobDownloadResult(BinaryData.FromString("{\"documentId\":\"D-1\"}"));
        _blob.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns(Response.FromValue(result, Substitute.For<Response>()));

        var content = await _inbound.ReadAsync("c-1", TestContext.Current.CancellationToken);

        content.Should().Be("{\"documentId\":\"D-1\"}");
    }

    [Fact]
    public async Task ReadAsync_ABlobThatIsNotThere_ReadsAsNull()
    {
        _blob.DownloadContentAsync(Arg.Any<CancellationToken>()).Returns<Response<BlobDownloadResult>>(_ => throw new RequestFailedException(404, "The blob doesn't exist."));

        var content = await _inbound.ReadAsync("c-1", TestContext.Current.CancellationToken);

        content.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DeletesTheBlob_WhetherOrNotItIsStillThere()
    {
        await _inbound.DeleteAsync("c-1", TestContext.Current.CancellationToken);

        await _blob.Received(1).DeleteIfExistsAsync(cancellationToken: Arg.Any<CancellationToken>());
    }
}
