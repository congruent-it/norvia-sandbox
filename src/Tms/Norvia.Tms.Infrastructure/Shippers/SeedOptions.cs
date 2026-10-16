using System.ComponentModel.DataAnnotations;

namespace Norvia.Tms.Infrastructure.Shippers;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Path to the sandbox's seed file (seed/norvia.seed.json).</summary>
    [Required]
    public string Path { get; init; } = string.Empty;

    /// <summary>Whether the TMS books the demo's days (Over capacity day, Trip day, Trip execution) when it first starts. Off, only the Simulator or a person creates Transport Orders.</summary>
    public bool DemoData { get; init; } = true;
}
