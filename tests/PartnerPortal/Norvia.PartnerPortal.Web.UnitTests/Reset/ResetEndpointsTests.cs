using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Reset;
using Norvia.PartnerPortal.Web.Reset;

namespace Norvia.PartnerPortal.Web.UnitTests.Reset;

public sealed class ResetEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IOptions<ResetEndpointOptions> _options = Options.Create(new ResetEndpointOptions { ApiKey = "reset-secret" });

    [Fact]
    public async Task Reset_WithTheKey_ReportsWhatWasDeleted()
    {
        var report = new ResetReport([new DeletedKind("Bookings", 3)]);
        _mediator.Send(Arg.Any<ResetEstateCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(report));

        var result = await ResetEndpoints.ResetAsync("reset-secret", _options, _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ResetReport>>().Which.Value.Should().Be(report);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("another-key")]
    public async Task Reset_WithoutTheKey_IsUnauthorizedAndDeletesNothing(string? apiKey)
    {
        var result = await ResetEndpoints.ResetAsync(apiKey, _options, _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceive().Send(Arg.Any<ResetEstateCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_NoKeyConfigured_RefusesEveryCaller()
    {
        var result = await ResetEndpoints.ResetAsync("anything", Options.Create(new ResetEndpointOptions()), _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task Reset_SwitchedOff_IsAForbiddenProblemWithItsCode()
    {
        _mediator.Send(Arg.Any<ResetEstateCommand>(), Arg.Any<CancellationToken>()).Returns(Result<ResetReport>.Forbidden("The reset is switched off for the Partner portal."));

        var result = await ResetEndpoints.ResetAsync("reset-secret", _options, _mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Which;
        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Extensions["code"].Should().Be("ResetDisabled");
        problem.ProblemDetails.Detail.Should().Contain("switched off");
    }

    [Fact]
    public async Task GetReadiness_WithTheKey_AnswersTheSetting()
    {
        _mediator.Send(Arg.Any<GetResetReadinessQuery>(), Arg.Any<CancellationToken>()).Returns(new ResetReadiness(true, null));

        var result = await ResetEndpoints.GetReadinessAsync("reset-secret", _options, _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ResetReadiness>>().Which.Value.Should().Be(new ResetReadiness(true, null));
    }

    [Fact]
    public async Task GetReadiness_WithoutTheKey_IsUnauthorized()
    {
        var result = await ResetEndpoints.GetReadinessAsync(null, _options, _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
    }
}
