using Microsoft.AspNetCore.Http;
using Norvia.Simulator.Web.Operators;

namespace Norvia.Simulator.Web.UnitTests.Operators;

public sealed class SimulatorGateTests
{
    private static async Task<(DefaultHttpContext Context, bool Reached, string Body)> Send(bool enabled, string path)
    {
        var reached = false;
        var gate = new SimulatorGate(_ => { reached = true; return Task.CompletedTask; }, enabled);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await gate.InvokeAsync(context);

        context.Response.Body.Position = 0;
        return (context, reached, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/plan")]
    [InlineData("/runs/1")]
    public async Task Invoke_WhenEnabled_LetsEveryRequestThrough(string path)
    {
        var (_, reached, _) = await Send(enabled: true, path);

        reached.Should().BeTrue();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/plan")]
    [InlineData("/_blazor")]
    public async Task Invoke_WhenSwitchedOff_AnswersNotFoundAndSaysWhy(string path)
    {
        var (context, reached, body) = await Send(enabled: false, path);

        reached.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        body.Should().Be(SimulatorGate.OffText).And.Contain("Simulator:Enabled");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/ALIVE")]
    public async Task Invoke_WhenSwitchedOff_StillAnswersTheHealthChecks(string path)
    {
        var (_, reached, _) = await Send(enabled: false, path);

        reached.Should().BeTrue();
    }
}
