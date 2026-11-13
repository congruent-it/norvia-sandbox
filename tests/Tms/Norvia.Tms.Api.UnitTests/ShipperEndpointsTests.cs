using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Shippers;
using Norvia.Tms.Application.Shippers.Replace;
using NSubstitute;

namespace Norvia.Tms.Api.UnitTests;

public sealed class ShipperEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private void Answer(Result<ReplaceShipperResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReplaceShipperCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Replace_AnAppliedOrStaleRevision_AnswersOk()
    {
        Answer(new ReplaceShipperResult(Applied: false, 3));

        var result = await ShipperEndpoints.ReplaceAsync("SHP-0001", new ReplaceShipperRequest("Name", true, 2), _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ReplaceShipperResult>>().Which.Value.Should().Be(new ReplaceShipperResult(false, 3));
    }

    [Fact]
    public async Task Replace_AnUnreadableRecord_AnswersABadRequestWithItsCode()
    {
        Answer(Result<ReplaceShipperResult>.Invalid(new ValidationError("Shipper", "unreadable", "MasterDataUnreadable", ValidationSeverity.Error)));

        var result = await ShipperEndpoints.ReplaceAsync("SHP-0001", new ReplaceShipperRequest("", true, 0), _mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataUnreadable");
    }
}
