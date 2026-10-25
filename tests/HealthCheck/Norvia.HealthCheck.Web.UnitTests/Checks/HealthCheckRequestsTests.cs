// NSubstitute sets up a ValueTask-returning call by calling it and configuring its result; nothing is left unawaited.
#pragma warning disable CA2012
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Run;
using Norvia.HealthCheck.Web.Checks;
using TestContext = Xunit.TestContext;

namespace Norvia.HealthCheck.Web.UnitTests.Checks;

public sealed class HealthCheckRequestsTests
{
    [Fact]
    public async Task RunAsync_And_NarrateAsync_SendTheQueryInAScopeOfTheirOwn()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<RunHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new HealthCheckReport("fixed", DateTimeOffset.UnixEpoch, 1, []))));
        mediator.Send(Arg.Any<NarrateHealthCheckQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new Narration("Nothing.", []))));
        await using var services = new ServiceCollection().AddScoped(_ => mediator).BuildServiceProvider();
        var requests = new HealthCheckRequests(services.GetRequiredService<IServiceScopeFactory>());

        var run = await requests.RunAsync(new RunHealthCheckQuery(), TestContext.Current.CancellationToken);
        var narration = await requests.NarrateAsync(new NarrateHealthCheckQuery("run-1"), TestContext.Current.CancellationToken);

        run.Value.Estate.Should().Be("fixed");
        narration.Value.Summary.Should().Be("Nothing.");
    }
}
