namespace Norvia.Simulator.Domain.Runs;

public static class Presets
{
    public static IReadOnlyList<Preset> All { get; } =
    [
        new("Quiet Monday", "A handful of bookings, sent at once.", 20, 5, 30, RunMode.Burst, TimeSpan.Zero),
        new("Busy day", "A hundred bookings spread over ten minutes.", 100, 10, 30, RunMode.Paced, TimeSpan.FromMinutes(10)),
        new("Peak week", "Four hundred bookings spread over half an hour, with more of them wrong.", 400, 15, 20, RunMode.Paced, TimeSpan.FromMinutes(30)),
    ];

    public static Preset? Find(string? name) => All.FirstOrDefault(preset => preset.Name == name);
}
