using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Read;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>Reading a Proof of delivery file whose content the retention period deleted (P6).</summary>
public sealed class ProofOfDeliveryFileEndpointTests
{
    private static readonly Guid FileId = Guid.Parse("6f1c2f55-4a3f-4a38-9a54-2b7a1f0f7a11");

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Fact]
    public async Task GetFile_AnExpiredFile_IsGoneWithTheExpiredCode()
    {
        _mediator.Send(new GetProofOfDeliveryFileQuery("SH-0000012", FileId), Arg.Any<CancellationToken>()).Returns(ProofOfDeliveryFileExpiry.Expired());

        var result = await ProofOfDeliveryEndpoints.GetFileAsync("SH-0000012", FileId, _mediator, TestContext.Current.CancellationToken);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(410);
        problem.ProblemDetails.Extensions["code"].Should().Be("ProofOfDeliveryFileExpired");
    }

    [Fact]
    public async Task GetFile_AnUnknownFile_IsNotFound()
    {
        _mediator.Send(new GetProofOfDeliveryFileQuery("SH-0000012", FileId), Arg.Any<CancellationToken>()).Returns(Result<ProofOfDeliveryFileContent>.NotFound());

        var result = await ProofOfDeliveryEndpoints.GetFileAsync("SH-0000012", FileId, _mediator, TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetFile_AKnownFile_IsStreamed()
    {
        _mediator.Send(new GetProofOfDeliveryFileQuery("SH-0000012", FileId), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ProofOfDeliveryFileContent("cmr.pdf", "application/pdf", new MemoryStream([1]))));

        var result = await ProofOfDeliveryEndpoints.GetFileAsync("SH-0000012", FileId, _mediator, TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<FileStreamHttpResult>();
    }
}
