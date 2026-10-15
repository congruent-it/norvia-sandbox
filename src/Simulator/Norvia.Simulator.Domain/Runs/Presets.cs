using Norvia.Simulator.Domain.Carriers;

namespace Norvia.Simulator.Domain.Runs;

public static class Presets
{
    public static IReadOnlyList<Preset> All { get; } =
    [
        new("Quiet Monday", "A handful of bookings, sent at once.", 20, 5, 30, RunMode.Burst, TimeSpan.Zero, AssignedPercent: 90, OwnFleetPercent: 60, ReassignPercent: 5, Carriers: new CarrierShares(ReleasePercent: 100, ConfirmPercent: 80, DeclinePercent: 10, TrackedPercent: 90, ExceptionPercent: 10, DeliveredPercent: 80, ProofOfDeliveryPercent: 90, CancelPercent: 50)),
        new("Busy day", "A hundred bookings spread over ten minutes.", 100, 10, 30, RunMode.Paced, TimeSpan.FromMinutes(10), AssignedPercent: 80, OwnFleetPercent: 50, ReassignPercent: 5, Carriers: new CarrierShares()),
        new("Peak week", "Four hundred bookings spread over half an hour, with more of them wrong.", 400, 15, 20, RunMode.Paced, TimeSpan.FromMinutes(30), AssignedPercent: 70, OwnFleetPercent: 30, ReassignPercent: 10, Carriers: new CarrierShares(ReleasePercent: 80, ConfirmPercent: 60, DeclinePercent: 20, TrackedPercent: 85, ExceptionPercent: 20, DeliveredPercent: 70, ProofOfDeliveryPercent: 80, CancelPercent: 40)),
    ];

    public static Preset? Find(string? name) => All.FirstOrDefault(preset => preset.Name == name);
}
