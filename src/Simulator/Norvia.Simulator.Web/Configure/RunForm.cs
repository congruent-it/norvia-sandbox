using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Domain.Carriers;
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
        Carriers = new CarrierShares(
            ReleasePercent, ConfirmPercent, DeclinePercent, TrackedPercent, ExceptionPercent, DeliveredPercent, ProofOfDeliveryPercent, CancelPercent),
    };

    public void Fill(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        Preset = preset.Name;
        Orders = preset.OrderCount;
        BadPercent = preset.BadSharePercent;
        TodayPercent = preset.CollectedTodayPercent;
        AssignedPercent = preset.AssignedPercent;
        OwnFleetPercent = preset.OwnFleetPercent;
        ReassignPercent = preset.ReassignPercent;
        var carriers = preset.Carriers ?? new CarrierShares();
        ReleasePercent = carriers.ReleasePercent;
        ConfirmPercent = carriers.ConfirmPercent;
        DeclinePercent = carriers.DeclinePercent;
        TrackedPercent = carriers.TrackedPercent;
        ExceptionPercent = carriers.ExceptionPercent;
        DeliveredPercent = carriers.DeliveredPercent;
        ProofOfDeliveryPercent = carriers.ProofOfDeliveryPercent;
        CancelPercent = carriers.CancelPercent;
        Mode = preset.Mode.ToString();
        WindowMinutes = preset.Mode == RunMode.Paced ? (int)preset.Window.TotalMinutes : WindowMinutes;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        Limits is null ? [] : ToSettings().Problems(Limits).Select(problem => new ValidationResult(problem));
}
