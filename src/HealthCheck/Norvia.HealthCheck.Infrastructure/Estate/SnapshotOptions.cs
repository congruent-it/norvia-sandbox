using System.ComponentModel.DataAnnotations;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>Which snapshot the local health check reads: the fixed estate or one of the as-found presets of <c>infra/presets</c>.</summary>
public sealed class SnapshotOptions
{
    public const string SectionName = "Estate";

    public const string AsFound = "as-found";
    public const string Fixed = "fixed";
    public const string Contrast = "contrast";
    public const string LookAlike = "look-alike";
    public const string SilentCarrier = "silent-carrier";

    /// <summary>Every snapshot there is, one for the fixed estate and one for each preset.</summary>
    public static readonly IReadOnlyList<string> All = [AsFound, Fixed, Contrast, LookAlike, SilentCarrier];

    /// <summary>The snapshot's name, one of <see cref="All"/>. The AppHost sets it; it defaults to the estate as found, which is the one worth finding things in.</summary>
    [Required]
    [RegularExpression("^(as-found|fixed|contrast|look-alike|silent-carrier)$", ErrorMessage = "Estate:Snapshot must be as-found, fixed, contrast, look-alike or silent-carrier.")]
    public string Snapshot { get; set; } = AsFound;
}
