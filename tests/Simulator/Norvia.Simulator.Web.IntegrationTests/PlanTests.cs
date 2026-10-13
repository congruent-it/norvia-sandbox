extern alias SimulatorWeb;

using Ardalis.Result;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

[Collection("Simulator runs")]
public sealed class PlanTests(SimulatorFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_OfTheSameSeedTwice_IsTheSamePlan()
    {
        var settings = Runs.Burst(orders: 25, badSharePercent: 20, Runs.NextSeed());

        var first = (await fixture.Simulator.Requests.PreviewAsync(settings, Ct)).Value;
        var second = (await fixture.Simulator.Requests.PreviewAsync(settings, Ct)).Value;

        second.Rows.Should().Equal(first.Rows);
    }

    [Fact]
    public async Task Preview_OfAnotherSeed_IsAnotherPlan()
    {
        var seed = Runs.NextSeed();

        var first = (await fixture.Simulator.Requests.PreviewAsync(Runs.Burst(25, 20, seed), Ct)).Value;
        var second = (await fixture.Simulator.Requests.PreviewAsync(Runs.Burst(25, 20, seed + 1), Ct)).Value;

        second.Rows.Select(row => (row.Route, row.WeightKg, row.Bad)).Should().NotEqual(first.Rows.Select(row => (row.Route, row.WeightKg, row.Bad)));
    }

    [Fact]
    public async Task Preview_WithABadShare_HoldsExactlyThatManyBadBookings()
    {
        var preview = (await fixture.Simulator.Requests.PreviewAsync(Runs.Burst(orders: 40, badSharePercent: 25, Runs.NextSeed()), Ct)).Value;

        preview.Rows.Should().HaveCount(40);
        preview.BadBookings.Should().Be(10);
    }

    [Fact]
    public async Task Preview_PromisesWhatARunSends()
    {
        var settings = Runs.Burst(orders: 12, badSharePercent: 25, Runs.NextSeed());
        var preview = (await fixture.Simulator.Requests.PreviewAsync(settings, Ct)).Value;

        var run = await fixture.Simulator.Requests.StartAsync(settings, Ct);
        await fixture.Simulator.Requests.FinishedAsync(run.Id, Ct);

        run.Number.Should().Be(preview.RunNumber);
        var sent = (await fixture.Simulator.Requests.LogAsync(run.Id, Ct)).Where(Runs.IsBooking).ToList();
        sent.Select(line => line.ShipperReference).Should().BeEquivalentTo(preview.Rows.Select(row => row.Reference));
        sent.Select(line => line.Actor).Should().BeEquivalentTo(preview.Rows.Select(row => row.Actor));
    }

    [Fact]
    public async Task APlanOverTheCap_IsRefusedByThePreviewAndByTheStart_AndNothingIsStarted()
    {
        var limits = (await fixture.Simulator.Requests.GetConfigureAsync(Ct)).Limits;
        var runsBefore = (await fixture.Simulator.Requests.ListRunsAsync(Ct)).Count;
        var tooMany = Runs.Burst(limits.MaxOrdersPerRun + 1, badSharePercent: 0, Runs.NextSeed());

        var preview = await fixture.Simulator.Requests.PreviewAsync(tooMany, Ct);
        var started = await fixture.Simulator.Requests.StartAsync(tooMany, "integration tests", Ct);

        preview.Status.Should().Be(ResultStatus.Invalid);
        started.Status.Should().Be(ResultStatus.Invalid);
        started.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain($"at most {limits.MaxOrdersPerRun}");
        (await fixture.Simulator.Requests.ListRunsAsync(Ct)).Should().HaveCount(runsBefore);
    }
}
