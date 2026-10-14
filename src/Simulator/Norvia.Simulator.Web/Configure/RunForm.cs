using System.ComponentModel.DataAnnotations;
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
        AssignedPercent, OwnFleetPercent, ReassignPercent);

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
        Mode = preset.Mode.ToString();
        WindowMinutes = preset.Mode == RunMode.Paced ? (int)preset.Window.TotalMinutes : WindowMinutes;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        Limits is null ? [] : ToSettings().Problems(Limits).Select(problem => new ValidationResult(problem));
}
