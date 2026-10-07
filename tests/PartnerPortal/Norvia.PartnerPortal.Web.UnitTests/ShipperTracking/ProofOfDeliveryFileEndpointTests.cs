using Microsoft.AspNetCore.Http;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.ShipperTracking;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperTracking;

public sealed class ProofOfDeliveryFileEndpointTests
{
    private static readonly Guid FileId = TrackingSamples.CmrFile;
    private readonly IPartnerPortal _portal = Substitute.For<IPartnerPortal>();

    [Fact]
    public async Task Download_AFileNorviaStreams_IsSentWithItsNameAndType()
    {
        _portal.DownloadProofOfDeliveryFileAsync("brouwer-dranken", "TO-0000042", FileId, Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Found(new ShipperProofFileContent("CMR signed.pdf", "application/pdf", [1, 2, 3])));

        var result = await ProofOfDeliveryFileEndpoint.DownloadAsync("brouwer-dranken", "TO-0000042", FileId, _portal, Xunit.TestContext.Current.CancellationToken);

        var file = result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>().Subject;
        (file.ContentType, file.FileDownloadName, file.FileContents.ToArray()).Should().BeEquivalentTo(("application/pdf", "CMR signed.pdf", new byte[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData(TrackingOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrackingOutcome.Refused, StatusCodes.Status502BadGateway)]
    [InlineData(TrackingOutcome.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Download_AFailure_IsAProblemWithTheMatchingStatus(TrackingOutcome outcome, int status)
    {
        _portal.DownloadProofOfDeliveryFileAsync("brouwer-dranken", "TO-0000042", FileId, Arg.Any<CancellationToken>())
            .Returns(TrackingAnswer.Failed<ShipperProofFileContent>(outcome, "Why not."));

        var result = await ProofOfDeliveryFileEndpoint.DownloadAsync("brouwer-dranken", "TO-0000042", FileId, _portal, Xunit.TestContext.Current.CancellationToken);

        var problem = result.Result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        (problem.StatusCode, problem.ProblemDetails.Detail).Should().Be((status, "Why not."));
    }

    [Fact]
    public void MapProofOfDeliveryFileEndpoint_RoutesTheDownloadLinkTheDetailPageGives()
    {
        var path = Norvia.PartnerPortal.Web.Personas.PersonaChoice.ProofOfDeliveryFilePath("brouwer-dranken", "TO-0000042", FileId);

        path.Should().Be($"/personas/brouwer-dranken/tracking/TO-0000042/proof-of-delivery/files/{FileId}");
        ProofOfDeliveryFileEndpoint.Route.Should().Be("/personas/{personaId}/tracking/{transportOrderNumber}/proof-of-delivery/files/{fileId:guid}");
    }
}
