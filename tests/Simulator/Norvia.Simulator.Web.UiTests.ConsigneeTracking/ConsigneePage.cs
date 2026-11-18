using Microsoft.Playwright;

namespace Norvia.Simulator.Web.UiTests.ConsigneeTracking;

/// <summary>A Consignee's browser: a context of its own that holds nothing of the Simulator or the Planner, and has no sign-in.</summary>
internal sealed class ConsigneePage : IAsyncDisposable
{
    private readonly IBrowserContext _context;

    private ConsigneePage(IBrowserContext context, IPage page)
    {
        _context = context;
        Page = page;
    }

    public IPage Page { get; }

    public static async Task<ConsigneePage> OpenAsync(SimulatorUiFixture simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        var context = await simulator.Browser.NewContextAsync();
        return new ConsigneePage(context, await context.NewPageAsync());
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();
}
