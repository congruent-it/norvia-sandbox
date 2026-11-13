using Microsoft.Extensions.Options;
using Norvia.HealthCheck.Infrastructure.Cost;

namespace Norvia.HealthCheck.Infrastructure.UnitTests.Cost;

public sealed class SnapshotCostReaderTests
{
    private static SnapshotCostReader Reader(string snapshot) => new(Options.Create(new CostSnapshotOptions { CostSnapshot = snapshot }));

    [Fact]
    public async Task ReadAsync_AnswersEachDayOfTheWindow_WithItsTransportOrdersAndItsSpendPerResourceInEur()
    {
        var days = await Reader("chatty-telemetry").ReadAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), TestContext.Current.CancellationToken);

        days.Select(day => day.Date).Should().Equal(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2));
        days[0].TransportOrders.Should().Be(38);
        days[0].Spend.Should().Contain(spend => spend.Resource == "log-workspace" && spend.Eur == 0.53m);
        days[1].TransportOrders.Should().Be(41);
    }

    [Fact]
    public async Task ReadAsync_HasNoLocalLag_SoNoDayIsProvisional()
    {
        var days = await Reader("fixed").ReadAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 14), TestContext.Current.CancellationToken);

        days.Should().NotBeEmpty().And.OnlyContain(day => !day.Provisional);
    }

    [Fact]
    public async Task ReadAsync_LeavesOutTheDaysOutsideTheWindow()
    {
        var days = await Reader("fixed").ReadAsync(new DateOnly(2026, 10, 25), new DateOnly(2026, 11, 2), TestContext.Current.CancellationToken);

        days.Select(day => day.Date).Should().Equal(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2));
    }
}
