using System.ComponentModel.DataAnnotations;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>Which snapshot the local health check reads: the estate as found or the fixed one.</summary>
public sealed class SnapshotOptions
{
    public const string SectionName = "Estate";

    public const string AsFound = "as-found";
    public const string Fixed = "fixed";

    /// <summary>The snapshot's name, <c>as-found</c> or <c>fixed</c>. The AppHost sets it; it defaults to the estate as found, which is the one worth finding things in.</summary>
    [Required]
    [RegularExpression("^(as-found|fixed)$", ErrorMessage = "Estate:Snapshot must be as-found or fixed.")]
    public string Snapshot { get; set; } = AsFound;
}
