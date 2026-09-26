using System.ComponentModel.DataAnnotations;

namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>
/// How long the estate keeps what it traced: the estate Bicep's <c>integrationRetentionDays</c>, passed on to the
/// console. It is the estate's one retention setting (coding guidelines §7), so the console has none of its own.
/// </summary>
public sealed class IntegrationRetention
{
    public const string SectionName = "Estate";

    [Range(1, 730)]
    public int IntegrationRetentionDays { get; set; }

    public TimeSpan Period => TimeSpan.FromDays(IntegrationRetentionDays);
}
