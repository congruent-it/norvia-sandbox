using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.Configure;

/// <summary>
/// The Configure screen's form. A field out of range is named next to the field; what breaks a cap, which only the domain's
/// settings know, is named once for the whole form.
/// </summary>
public sealed class RunForm : IValidatableObject
{
    public const string CustomPreset = "Custom";

    public string Preset { get; set; } = CustomPreset;

    [Range(1, 100_000, ErrorMessage = "A run needs at least one order.")]
    public int Orders { get; set; } = 20;

    [Range(0, 100, ErrorMessage = "The share of bad bookings is a percentage from 0 to 100.")]
    public int BadPercent { get; set; } = 10;

    [Range(0, 100, ErrorMessage = "The share collected today is a percentage from 0 to 100.")]
    public int TodayPercent { get; set; } = 30;

    [Range(0, 100, ErrorMessage = "The share of orders the Planner assigns is a percentage from 0 to 100.")]
    public int AssignedPercent { get; set; } = SimulationSettings.DefaultAssignedPercent;

    [Range(0, 100, ErrorMessage = "The share of assignments to Own fleet is a percentage from 0 to 100.")]
    public int OwnFleetPercent { get; set; } = SimulationSettings.DefaultOwnFleetPercent;

    [Range(0, 100, ErrorMessage = "The share of assignments the Planner reassigns is a percentage from 0 to 100.")]
    public int ReassignPercent { get; set; } = SimulationSettings.DefaultReassignPercent;

    [Range(0, 100, ErrorMessage = "The share of Shipments the Planner releases is a percentage from 0 to 100.")]
    public int ReleasePercent { get; set; } = CarrierShares.DefaultReleasePercent;

    [Range(0, 100, ErrorMessage = "The share of released Shipments the Carriers confirm is a percentage from 0 to 100.")]
    public int ConfirmPercent { get; set; } = CarrierShares.DefaultConfirmPercent;

    [Range(0, 100, ErrorMessage = "The share of released Shipments the Carriers decline is a percentage from 0 to 100.")]
    public int DeclinePercent { get; set; } = CarrierShares.DefaultDeclinePercent;

    [Range(0, 100, ErrorMessage = "The share of confirmed Shipments the Carriers track is a percentage from 0 to 100.")]
    public int TrackedPercent { get; set; } = CarrierShares.DefaultTrackedPercent;

    [Range(0, 100, ErrorMessage = "The share of tracked Shipments that hit an Exception is a percentage from 0 to 100.")]
    public int ExceptionPercent { get; set; } = CarrierShares.DefaultExceptionPercent;

    [Range(0, 100, ErrorMessage = "The share of tracked Shipments that are delivered is a percentage from 0 to 100.")]
    public int DeliveredPercent { get; set; } = CarrierShares.DefaultDeliveredPercent;

    [Range(0, 100, ErrorMessage = "The share of delivered Shipments with a Proof of delivery is a percentage from 0 to 100.")]
    public int ProofOfDeliveryPercent { get; set; } = CarrierShares.DefaultProofOfDeliveryPercent;

    [Range(0, 100, ErrorMessage = "The share of failed collections the Planner cancels is a percentage from 0 to 100.")]
    public int CancelPercent { get; set; } = CarrierShares.DefaultCancelPercent;

    [Range(0, 100, ErrorMessage = "The share of Own fleet Shipments the Planner puts on Trips is a percentage from 0 to 100.")]
    public int TripPercent { get; set; } = SimulationSettings.DefaultTripPercent;

    [Range(0, 100, ErrorMessage = "The share of Shipments whose collection fails is a percentage from 0 to 100.")]
    public int CollectionFailedPercent { get; set; } = DriverShares.DefaultCollectionFailedPercent;

    [Range(0, 100, ErrorMessage = "The share of Shipments whose delivery fails is a percentage from 0 to 100.")]
    public int DeliveryFailedPercent { get; set; } = DriverShares.DefaultDeliveryFailedPercent;

    [Range(0, 100, ErrorMessage = "The share of Shipments the Driver reports damage on is a percentage from 0 to 100.")]
    public int DamagedPercent { get; set; } = DriverShares.DefaultDamagedPercent;

    [Range(0, 100, ErrorMessage = "The share of delivered Shipments with reservations is a percentage from 0 to 100.")]
    public int ReservationsPercent { get; set; } = DriverShares.DefaultReservationsPercent;

    [Range(0, 100, ErrorMessage = "The share of Shippers Finance invoices is a percentage from 0 to 100.")]
    public int InvoicePercent { get; set; } = InvoicingShares.DefaultInvoicePercent;

    [Range(0, 100, ErrorMessage = "The share of invoices Finance credits is a percentage from 0 to 100.")]
    public int CreditNotePercent { get; set; } = InvoicingShares.DefaultCreditNotePercent;

    [Range(0, 100, ErrorMessage = "The share of Carrier invoices with an edited amount is a percentage from 0 to 100.")]
    public int EditedCarrierInvoicePercent { get; set; } = InvoicingShares.DefaultEditedCarrierInvoicePercent;

    [Range(0, 100, ErrorMessage = "The share of Carrier invoice lines Finance rejects is a percentage from 0 to 100.")]
    public int RejectedCarrierLinePercent { get; set; } = InvoicingShares.DefaultRejectedCarrierLinePercent;

    /// <summary>The Carriers that write the opt-in hostile profile. Empty by default, and a Preset empties it: it is chosen on purpose, one Carrier at a time.</summary>
    public HashSet<string> HostileCarriers { get; } = new(StringComparer.Ordinal);

    /// <summary>The Drivers that write the opt-in hostile profile. Empty by default, and a Preset empties it.</summary>
    public HashSet<string> HostileDrivers { get; } = new(StringComparer.Ordinal);

    public bool IsHostile => HostileCarriers.Count > 0 || HostileDrivers.Count > 0;

    public void SetHostileCarrier(string id, bool on) => Set(HostileCarriers, id, on);

    public void SetHostileDriver(string id, bool on) => Set(HostileDrivers, id, on);

    private static void Set(HashSet<string> ids, string id, bool on)
    {
        if (on)
        {
            ids.Add(id);
        }
        else
        {
            ids.Remove(id);
        }
    }

    public string Mode { get; set; } = nameof(RunMode.Burst);

    [Range(1, SimulationSettings.MaxWindowMinutes, ErrorMessage = "A paced run is spread over 1 to 240 minutes.")]
    public int WindowMinutes { get; set; } = 10;

    public int Seed { get; set; }

    public SimulationLimits? Limits { get; set; }

    public bool IsPaced => Mode == nameof(RunMode.Paced);

    public SimulationSettings ToSettings() => new(
        Orders, BadPercent, TodayPercent, Seed, Enum.Parse<RunMode>(Mode),
        IsPaced ? TimeSpan.FromMinutes(WindowMinutes) : TimeSpan.Zero,
        Preset == CustomPreset ? null : Preset,
        AssignedPercent, OwnFleetPercent, ReassignPercent)
    {
        TripPercent = TripPercent,
        Carriers = new CarrierShares(
            ReleasePercent, ConfirmPercent, DeclinePercent, TrackedPercent, ExceptionPercent, DeliveredPercent, ProofOfDeliveryPercent, CancelPercent),
        Drivers = new DriverShares(CollectionFailedPercent, DeliveryFailedPercent, DamagedPercent, ReservationsPercent),
        Invoicing = new InvoicingShares(InvoicePercent, CreditNotePercent, EditedCarrierInvoicePercent, RejectedCarrierLinePercent),
        HostileCarrierIds = [.. HostileCarriers.Order(StringComparer.Ordinal)],
        HostileDriverIds = [.. HostileDrivers.Order(StringComparer.Ordinal)],
    };

    public void Fill(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Preset = preset.Name;
        // No Preset holds the hostile profile, and choosing one never carries it over from the last: the hostile profile is switched on again on purpose.
        HostileCarriers.Clear();
        HostileDrivers.Clear();
        Orders = preset.OrderCount;
        BadPercent = preset.BadSharePercent;
        TodayPercent = preset.CollectedTodayPercent;
        AssignedPercent = preset.AssignedPercent;
        OwnFleetPercent = preset.OwnFleetPercent;
        ReassignPercent = preset.ReassignPercent;
        TripPercent = SimulationSettings.DefaultTripPercent;
        var carriers = preset.Carriers ?? new CarrierShares();
        ReleasePercent = carriers.ReleasePercent;
        ConfirmPercent = carriers.ConfirmPercent;
        DeclinePercent = carriers.DeclinePercent;
        TrackedPercent = carriers.TrackedPercent;
        ExceptionPercent = carriers.ExceptionPercent;
        DeliveredPercent = carriers.DeliveredPercent;
        ProofOfDeliveryPercent = carriers.ProofOfDeliveryPercent;
        CancelPercent = carriers.CancelPercent;
        var drivers = preset.Drivers ?? new DriverShares();
        CollectionFailedPercent = drivers.CollectionFailedPercent;
        DeliveryFailedPercent = drivers.DeliveryFailedPercent;
        DamagedPercent = drivers.DamagedPercent;
        ReservationsPercent = drivers.ReservationsPercent;
        var invoicing = preset.Invoicing ?? new InvoicingShares();
        InvoicePercent = invoicing.InvoicePercent;
        CreditNotePercent = invoicing.CreditNotePercent;
        EditedCarrierInvoicePercent = invoicing.EditedCarrierInvoicePercent;
        RejectedCarrierLinePercent = invoicing.RejectedCarrierLinePercent;
        Mode = preset.Mode.ToString();
        WindowMinutes = preset.Mode == RunMode.Paced ? (int)preset.Window.TotalMinutes : WindowMinutes;
    }

    /// <summary>Fills the form with the settings of an earlier run. A Preset that is no longer offered shows as Custom.</summary>
    public void Fill(SimulationSettings settings, IEnumerable<Preset> presets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(presets);

        Preset = presets.Any(preset => preset.Name == settings.PresetName) ? settings.PresetName! : CustomPreset;
        Orders = settings.OrderCount;
        BadPercent = settings.BadSharePercent;
        TodayPercent = settings.CollectedTodayPercent;
        AssignedPercent = settings.AssignedPercent;
        OwnFleetPercent = settings.OwnFleetPercent;
        ReassignPercent = settings.ReassignPercent;
        TripPercent = settings.TripPercent;
        ReleasePercent = settings.Carriers.ReleasePercent;
        ConfirmPercent = settings.Carriers.ConfirmPercent;
        DeclinePercent = settings.Carriers.DeclinePercent;
        TrackedPercent = settings.Carriers.TrackedPercent;
        ExceptionPercent = settings.Carriers.ExceptionPercent;
        DeliveredPercent = settings.Carriers.DeliveredPercent;
        ProofOfDeliveryPercent = settings.Carriers.ProofOfDeliveryPercent;
        CancelPercent = settings.Carriers.CancelPercent;
        CollectionFailedPercent = settings.Drivers.CollectionFailedPercent;
        DeliveryFailedPercent = settings.Drivers.DeliveryFailedPercent;
        DamagedPercent = settings.Drivers.DamagedPercent;
        ReservationsPercent = settings.Drivers.ReservationsPercent;
        InvoicePercent = settings.Invoicing.InvoicePercent;
        CreditNotePercent = settings.Invoicing.CreditNotePercent;
        EditedCarrierInvoicePercent = settings.Invoicing.EditedCarrierInvoicePercent;
        RejectedCarrierLinePercent = settings.Invoicing.RejectedCarrierLinePercent;
        HostileCarriers.Clear();
        HostileCarriers.UnionWith(settings.HostileCarrierIds);
        HostileDrivers.Clear();
        HostileDrivers.UnionWith(settings.HostileDriverIds);
        Mode = settings.Mode.ToString();
        WindowMinutes = settings.Mode == RunMode.Paced ? (int)settings.Window.TotalMinutes : WindowMinutes;
        Seed = settings.Seed;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        Limits is null ? [] : ToSettings().Problems(Limits).Select(problem => new ValidationResult(problem));
}
