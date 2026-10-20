using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Reset;
using Norvia.Erp.Application.Reset;

namespace Norvia.Erp.Api.UnitTests;

public sealed class ResetEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private void Answer(Result<ResetReport> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ResetEstateCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task GetReadiness_Asked_AnswersWhatTheQueryAnswers()
    {
        var readiness = new ResetReadiness(true, "outbox (unpublished events)");
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<GetResetReadinessQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(readiness));
#pragma warning restore CA2012

        var result = await ResetEndpoints.GetReadinessAsync(_mediator, CancellationToken.None);

        result.Value.Should().Be(readiness);
    }

    [Fact]
    public async Task Reset_Done_AnswersWhatWasDeleted()
    {
        var report = new ResetReport([new DeletedKind("Shipper invoices", 3)]);
        Answer(report);

        var result = await ResetEndpoints.ResetAsync(_mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ResetReport>>().Which.Value.Should().Be(report);
    }

    [Fact]
    public async Task Reset_SwitchedOff_AnswersA403ProblemWithTheResetDisabledCode()
    {
        Answer(Result<ResetReport>.Forbidden("The reset is switched off for the ERP."));

        var result = await ResetEndpoints.ResetAsync(_mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["code"].Should().Be(ResetEstateHandler.DisabledCode);
    }

    [Fact]
    public async Task Reset_NotQuiet_AnswersA409ProblemWithTheSubscriptionBusyCode()
    {
        Answer(Result<ResetReport>.Conflict("The subscription x/y holds unprocessed messages."));

        var result = await ResetEndpoints.ResetAsync(_mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Extensions["code"].Should().Be(ResetEstateHandler.SubscriptionBusyCode);
    }
}
