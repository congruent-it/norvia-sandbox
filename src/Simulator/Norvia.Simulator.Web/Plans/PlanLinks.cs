using System.Globalization;
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
    public static string Plan(SimulationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"orders={settings.OrderCount}&bad={settings.BadSharePercent}&today={settings.CollectedTodayPercent}&seed={settings.Seed}&mode={settings.Mode}&window={(int)settings.Window.TotalMinutes}"
            + $"&assigned={settings.AssignedPercent}&ownfleet={settings.OwnFleetPercent}&reassign={settings.ReassignPercent}&trips={settings.TripPercent}"
            + $"&release={settings.Carriers.ReleasePercent}&confirm={settings.Carriers.ConfirmPercent}&decline={settings.Carriers.DeclinePercent}&tracked={settings.Carriers.TrackedPercent}"
            + $"&exception={settings.Carriers.ExceptionPercent}&delivered={settings.Carriers.DeliveredPercent}&pod={settings.Carriers.ProofOfDeliveryPercent}&cancel={settings.Carriers.CancelPercent}"
            + $"&collfail={settings.Drivers.CollectionFailedPercent}&delfail={settings.Drivers.DeliveryFailedPercent}&damaged={settings.Drivers.DamagedPercent}&reservations={settings.Drivers.ReservationsPercent}");
        return settings.PresetName is { } preset ? $"/plan?{query}&preset={Uri.EscapeDataString(preset)}" : $"/plan?{query}";
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
