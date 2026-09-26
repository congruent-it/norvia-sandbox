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

    /// <summary>
    /// How long Logic Apps run history keeps a run and its bodies: the same period, as the estate Bicep sets it, but no
    /// more than the 365 days Logic Apps Standard keeps at most.
    /// </summary>
    public int RunHistoryDays => Math.Min(IntegrationRetentionDays, 365);
}
