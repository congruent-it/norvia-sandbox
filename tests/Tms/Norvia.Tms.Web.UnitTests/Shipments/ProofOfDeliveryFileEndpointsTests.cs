using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Web.Shipments;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class ProofOfDeliveryFileEndpointsTests
{
    private static readonly Guid FileId = Guid.Parse("6f1c2f55-4a3f-4a38-9a54-2b7a1f0f7a11");

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Fact]
    public async Task Get_AKnownFile_ShowsItInTheBrowser()
    {
        Found();

        var result = await ProofOfDeliveryFileEndpoints.GetAsync("SH-0000012", FileId, null, _mediator, Xunit.TestContext.Current.CancellationToken);

        var file = result.Result.Should().BeOfType<FileStreamHttpResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().BeNull();
    }

    [Fact]
    public async Task Get_WhenAskedToDownload_NamesTheFileForSaving()
    {
        Found();

        var result = await ProofOfDeliveryFileEndpoints.GetAsync("SH-0000012", FileId, true, _mediator, Xunit.TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<FileStreamHttpResult>().Which.FileDownloadName.Should().Be("cmr.pdf");
    }

    [Fact]
    public async Task Get_AnUnknownFile_IsNotFound()
    {
        _mediator.Send(new GetProofOfDeliveryFileQuery("SH-0000012", FileId), Arg.Any<CancellationToken>()).Returns(Result<ProofOfDeliveryFileContent>.NotFound());

        var result = await ProofOfDeliveryFileEndpoints.GetAsync("SH-0000012", FileId, null, _mediator, Xunit.TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_AFileTheRetentionPeriodDeleted_IsGoneWithAnExpiredMessage_NotAnError()
    {
        _mediator.Send(new GetProofOfDeliveryFileQuery("SH-0000012", FileId), Arg.Any<CancellationToken>()).Returns(ProofOfDeliveryFileExpiry.Expired());

        var result = await ProofOfDeliveryFileEndpoints.GetAsync("SH-0000012", FileId, null, _mediator, Xunit.TestContext.Current.CancellationToken);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(410);
        problem.ProblemDetails.Title.Should().Contain("expired");
    }

    [Fact]
    public void PathOf_NamesTheFileOfAShipmentAndWhetherToDownloadIt()
    {
        ProofOfDeliveryFileEndpoints.PathOf("SH-0000012", FileId).Should().Be($"/shipments/SH-0000012/proof-of-delivery/files/{FileId}");
        ProofOfDeliveryFileEndpoints.PathOf("SH-0000012", FileId, download: true).Should().Be($"/shipments/SH-0000012/proof-of-delivery/files/{FileId}?download=true");
    }

    private void Found() =>
        _mediator.Send(new GetProofOfDeliveryFileQuery("SH-0000012", FileId), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ProofOfDeliveryFileContent("cmr.pdf", "application/pdf", new MemoryStream([1, 2, 3]))));
}
