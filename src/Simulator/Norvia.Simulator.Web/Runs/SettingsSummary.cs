using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.Runs;

public sealed record SettingGroup(string Title, IReadOnlyList<(string Label, string Value)> Settings);

/// <summary>The settings of a run as the Run page lists them: grouped, and worded, as on the Configure screen.</summary>
public static class SettingsSummary
{
    private static string Percent(int value) => $"{value}%";

    public static IReadOnlyList<SettingGroup> Of(SimulationSettings settings) =>
    [
        new("Shippers",
        [
            ("Orders", settings.OrderCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Bad bookings (%)", Percent(settings.BadSharePercent)),
            ("Collected today (%)", Percent(settings.CollectedTodayPercent)),
        ]),
        new("Planner",
        [
            ("Assigned by the Planner (%)", Percent(settings.AssignedPercent)),
            ("To Own fleet where it fits (%)", Percent(settings.OwnFleetPercent)),
            ("Reassigned (%)", Percent(settings.ReassignPercent)),
            ("Released by the Planner (%)", Percent(settings.Carriers.ReleasePercent)),
        ]),
        new("Carriers",
        [
            ("Confirmed by the Carrier (%)", Percent(settings.Carriers.ConfirmPercent)),
            ("Declined by the Carrier (%)", Percent(settings.Carriers.DeclinePercent)),
            ("Tracked (%)", Percent(settings.Carriers.TrackedPercent)),
            ("Exceptions on the way (%)", Percent(settings.Carriers.ExceptionPercent)),
            ("Delivered (%)", Percent(settings.Carriers.DeliveredPercent)),
            ("With a Proof of delivery (%)", Percent(settings.Carriers.ProofOfDeliveryPercent)),
            ("Failed collections cancelled (%)", Percent(settings.Carriers.CancelPercent)),
        ]),
        new("Own fleet and Drivers",
        [
            ("Own fleet put on Trips (%)", Percent(settings.TripPercent)),
            ("Collections that fail (%)", Percent(settings.Drivers.CollectionFailedPercent)),
            ("Deliveries that fail (%)", Percent(settings.Drivers.DeliveryFailedPercent)),
            ("Damage reported (%)", Percent(settings.Drivers.DamagedPercent)),
            ("Delivered with reservations (%)", Percent(settings.Drivers.ReservationsPercent)),
        ]),
        new("Finance and invoicing",
        [
            ("Shippers invoiced (%)", Percent(settings.Invoicing.InvoicePercent)),
            ("Invoices credited in part (%)", Percent(settings.Invoicing.CreditNotePercent)),
            ("Carrier invoices with an edited amount (%)", Percent(settings.Invoicing.EditedCarrierInvoicePercent)),
            ("Carrier invoice lines rejected (%)", Percent(settings.Invoicing.RejectedCarrierLinePercent)),
        ]),
        new("How it is sent",
        [
            ("Sending", settings.Mode == RunMode.Paced ? $"Paced over {(int)settings.Window.TotalMinutes} minutes" : "Burst"),
            ("Seed", settings.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Preset", settings.PresetName ?? "Custom"),
        ]),
    ];
}
