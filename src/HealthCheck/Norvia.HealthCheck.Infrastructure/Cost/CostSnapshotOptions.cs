using System.ComponentModel.DataAnnotations;

namespace Norvia.HealthCheck.Infrastructure.Cost;

/// <summary>Which modelled cost snapshot the local health check reads: the flat estate, or the one where <c>chattyTelemetry</c> is on.</summary>
public sealed class CostSnapshotOptions
{
    public const string SectionName = "Estate";

    public const string Fixed = "fixed";
    public const string ChattyTelemetry = "chatty-telemetry";

    [Required]
    [RegularExpression("^(fixed|chatty-telemetry)$", ErrorMessage = "Estate:CostSnapshot must be fixed or chatty-telemetry.")]
    public string CostSnapshot { get; set; } = Fixed;
}
