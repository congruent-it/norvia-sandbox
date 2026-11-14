using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Api.Carriers;
using Norvia.Tms.Application.Carriers.Replace;
using NSubstitute;

namespace Norvia.Tms.Api.UnitTests;

public sealed class CarrierEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private void Answer(Result<ReplaceCarrierResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReplaceCarrierCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Replace_AnAppliedOrStaleRevision_AnswersOk()
    {
        Answer(new ReplaceCarrierResult(Applied: false, 3));

        var result = await CarrierEndpoints.ReplaceAsync("CAR-0001", new ReplaceCarrierRequest("Name", true, 2), _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ReplaceCarrierResult>>().Which.Value.Should().Be(new ReplaceCarrierResult(false, 3));
    }

    [Fact]
    public async Task Replace_AnUnreadableRecord_AnswersABadRequestWithItsCode()
    {
        Answer(Result<ReplaceCarrierResult>.Invalid(new ValidationError("Carrier", "unreadable", "MasterDataUnreadable", ValidationSeverity.Error)));

        var result = await CarrierEndpoints.ReplaceAsync("CAR-0001", new ReplaceCarrierRequest("", true, 0), _mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataUnreadable");
    }
}
