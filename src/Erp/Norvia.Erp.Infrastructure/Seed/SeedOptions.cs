using System.ComponentModel.DataAnnotations;

namespace Norvia.Erp.Infrastructure.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Path to the sandbox's seed file (seed/norvia.seed.json).</summary>
    [Required]
    public string Path { get; init; } = string.Empty;
}
