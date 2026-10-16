using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.Plans;

/// <summary>
/// The settings of a run in an address, so the Plan preview can be reloaded and shared, and Start run sends exactly what it
/// shows: the same settings and seed always give the same plan.
/// </summary>
public static class PlanLinks
{
    public static string Plan(SimulationSettings settings) => $"/plan?{Query(settings, keepSeed: true)}";

    /// <summary>The Configure screen filled with <paramref name="settings"/>, seed included.</summary>
    public static string Configure(SimulationSettings settings) => $"/?{Query(settings, keepSeed: true)}";

    /// <summary>The Configure screen filled with <paramref name="settings"/> but for the seed, which Configure offers anew.</summary>
    public static string ConfigureWithNewSeed(SimulationSettings settings) => $"/?{Query(settings, keepSeed: false)}";

    private static string Query(SimulationSettings settings, bool keepSeed)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var seed = keepSeed ? $"&seed={settings.Seed}" : "";
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"orders={settings.OrderCount}&bad={settings.BadSharePercent}&today={settings.CollectedTodayPercent}{seed}&mode={settings.Mode}&window={(int)settings.Window.TotalMinutes}"
            + $"&assigned={settings.AssignedPercent}&ownfleet={settings.OwnFleetPercent}&reassign={settings.ReassignPercent}&trips={settings.TripPercent}"
            + $"&release={settings.Carriers.ReleasePercent}&confirm={settings.Carriers.ConfirmPercent}&decline={settings.Carriers.DeclinePercent}&tracked={settings.Carriers.TrackedPercent}"
            + $"&exception={settings.Carriers.ExceptionPercent}&delivered={settings.Carriers.DeliveredPercent}&pod={settings.Carriers.ProofOfDeliveryPercent}&cancel={settings.Carriers.CancelPercent}"
            + $"&collfail={settings.Drivers.CollectionFailedPercent}&delfail={settings.Drivers.DeliveryFailedPercent}&damaged={settings.Drivers.DamagedPercent}&reservations={settings.Drivers.ReservationsPercent}");
        return settings.PresetName is { } preset ? $"{query}&preset={Uri.EscapeDataString(preset)}" : query;
    }

    /// <summary>The settings in the address of the Configure screen, or null when it holds none. An address without a seed gets <paramref name="newSeed"/>.</summary>
    public static SimulationSettings? ConfigureSettings(Uri address, int newSeed)
    {
        ArgumentNullException.ThrowIfNull(address);

        var query = QueryHelpers.ParseQuery(address.Query);
        string? Text(string name) => query.TryGetValue(name, out var value) ? value.ToString() : null;
        int? Number(string name) => int.TryParse(Text(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        return Settings(
            Number("orders"), Number("bad"), Number("today"), Number("seed") ?? newSeed, Text("mode"), Number("window"), Text("preset"),
            Number("assigned"), Number("ownfleet"), Number("reassign"),
            Number("release"), Number("confirm"), Number("decline"), Number("tracked"), Number("exception"), Number("delivered"), Number("pod"), Number("cancel"),
            Number("collfail"), Number("delfail"), Number("damaged"), Number("reservations"), Number("trips"));
    }

    public static string Run(Guid runId) => $"/runs/{runId}";

    /// <summary>The settings in the address, or null when it doesn't hold a full set. A link made before the Planner, the Carrier or the Driver stage existed has no such shares, and gets the defaults.</summary>
    public static SimulationSettings? Settings(
        int? orders, int? bad, int? today, int? seed, string? mode, int? window, string? preset,
        int? assigned = null, int? ownFleet = null, int? reassign = null,
        int? release = null, int? confirm = null, int? decline = null, int? tracked = null, int? exception = null, int? delivered = null, int? pod = null, int? cancel = null,
        int? collectionFailed = null, int? deliveryFailed = null, int? damaged = null, int? reservations = null, int? trips = null)
    {
        if (orders is null || bad is null || today is null || seed is null || !Enum.TryParse<RunMode>(mode, out var runMode))
        {
            return null;
        }

        return new SimulationSettings(orders.Value, bad.Value, today.Value, seed.Value, runMode, TimeSpan.FromMinutes(window ?? 0), preset,
            assigned ?? SimulationSettings.DefaultAssignedPercent,
            ownFleet ?? SimulationSettings.DefaultOwnFleetPercent,
            reassign ?? SimulationSettings.DefaultReassignPercent)
        {
            TripPercent = trips ?? SimulationSettings.DefaultTripPercent,
            Carriers = new CarrierShares(
                release ?? CarrierShares.DefaultReleasePercent,
                confirm ?? CarrierShares.DefaultConfirmPercent,
                decline ?? CarrierShares.DefaultDeclinePercent,
                tracked ?? CarrierShares.DefaultTrackedPercent,
                exception ?? CarrierShares.DefaultExceptionPercent,
                delivered ?? CarrierShares.DefaultDeliveredPercent,
                pod ?? CarrierShares.DefaultProofOfDeliveryPercent,
                cancel ?? CarrierShares.DefaultCancelPercent),
            Drivers = new DriverShares(
                collectionFailed ?? DriverShares.DefaultCollectionFailedPercent,
                deliveryFailed ?? DriverShares.DefaultDeliveryFailedPercent,
                damaged ?? DriverShares.DefaultDamagedPercent,
                reservations ?? DriverShares.DefaultReservationsPercent),
        };
    }
}
