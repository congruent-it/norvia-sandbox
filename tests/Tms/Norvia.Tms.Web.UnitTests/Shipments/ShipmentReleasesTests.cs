using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class ShipmentReleasesTests
{
    private readonly ITransportOrderCommands _commands = Substitute.For<ITransportOrderCommands>();
    private readonly ShipmentReleases _releases;

    public ShipmentReleasesTests()
    {
        _releases = new ShipmentReleases(_commands, NullLogger.Instance);
    }

    [Fact]
    public async Task ReleaseEachAsync_SeveralShipments_ReleasesEachOnItsOwnInTheOrderGiven()
    {
        _commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());
        _commands.ReleaseAsync("SH-0000013", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Refused("Shipment SH-0000013 isn't Planned."));
        _commands.ReleaseAsync("SH-0000014", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());

        var answers = await ReleaseEachAsync(["SH-0000012", "SH-0000013", "SH-0000014"]);

        answers.Should().Equal(
            ("SH-0000012", new ReleaseAnswer.Released()),
            ("SH-0000013", new ReleaseAnswer.Refused("Shipment SH-0000013 isn't Planned.")),
            ("SH-0000014", new ReleaseAnswer.Released()));
    }

    [Fact]
    public async Task ReleaseEachAsync_WhenTheTmsCantBeReachedForOne_AnswersNullForItAndReleasesTheOthers()
    {
        _commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>())
            .Returns<ReleaseAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));
        _commands.ReleaseAsync("SH-0000013", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());

        var answers = await ReleaseEachAsync(["SH-0000012", "SH-0000013"]);

        answers.Should().Equal(("SH-0000012", (ReleaseAnswer?)null), ("SH-0000013", new ReleaseAnswer.Released()));
    }

    [Fact]
    public async Task ReleaseEachAsync_WhenCancelled_StopsWithoutReleasingTheRest()
    {
        using var closed = new CancellationTokenSource();
        _commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await closed.CancelAsync();
            return (ReleaseAnswer)new ReleaseAnswer.Released();
        });

        var answers = new List<(string, ReleaseAnswer?)>();
        await foreach (var answer in _releases.ReleaseEachAsync(["SH-0000012", "SH-0000013"], closed.Token))
        {
            answers.Add(answer);
        }

        answers.Should().BeEmpty();
        await _commands.DidNotReceive().ReleaseAsync("SH-0000013", Arg.Any<CancellationToken>());
    }

    private async Task<List<(string, ReleaseAnswer?)>> ReleaseEachAsync(IReadOnlyList<string> numbers)
    {
        var answers = new List<(string, ReleaseAnswer?)>();
        await foreach (var answer in _releases.ReleaseEachAsync(numbers, Xunit.TestContext.Current.CancellationToken))
        {
            answers.Add(answer);
        }

        return answers;
    }
}
