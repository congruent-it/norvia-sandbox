using System.Globalization;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.Plans;

/// <summary>
/// The settings of a run in an address, so the Plan preview can be reloaded and shared, and Start run sends exactly what it
/// shows: the same settings and seed always give the same plan.
/// </summary>
public static class PlanLinks
{
    public static string Plan(SimulationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"orders={settings.OrderCount}&bad={settings.BadSharePercent}&today={settings.CollectedTodayPercent}&seed={settings.Seed}&mode={settings.Mode}&window={(int)settings.Window.TotalMinutes}");
        return settings.PresetName is { } preset ? $"/plan?{query}&preset={Uri.EscapeDataString(preset)}" : $"/plan?{query}";
    }

    public static string Run(Guid runId) => $"/runs/{runId}";

    /// <summary>The settings in the address, or null when it doesn't hold a full set.</summary>
    public static SimulationSettings? Settings(int? orders, int? bad, int? today, int? seed, string? mode, int? window, string? preset)
    {
        if (orders is null || bad is null || today is null || seed is null || !Enum.TryParse<RunMode>(mode, out var runMode))
        {
            return null;
        }

        return new SimulationSettings(orders.Value, bad.Value, today.Value, seed.Value, runMode, TimeSpan.FromMinutes(window ?? 0), preset);
    }
}
