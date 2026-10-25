using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>
/// The checklist engine: runs every Check over the estate's facts and makes a <see cref="Finding"/> for each Check that found something.
/// The Finding takes its severity, rule, toggle and fix from the Check, and its evidence is masked on the way out.
/// </summary>
public sealed class Checklist(IReadOnlyList<Check> checks)
{
    /// <summary>The checks of the estate's health check: one per as-found toggle (docs/operations/estate-health-check.md).</summary>
    public static Checklist Default { get; } = new(
    [
        new FailedRunAlertCheck(),
        new DeadLetterAlertCheck(),
        new AlertReceiverCheck(),
        new InlinedCarrierSecretCheck(),
        new AppInsightsRetentionCheck(),
        new TrackedPropertiesCheck(),
        new InboundLifecycleRuleCheck(),
        new TransitLifecycleRuleCheck(),
        new ProofFilesHotTierCheck(),
        new MalwareScanningCheck(),
        new StaleThresholdsCheck(),
        new ProofMissingThresholdCheck(),
        new LateDeliveryCheckCheck(),
        new ShipperCacheCheck(),
        new ShipperRateLimitCheck(),
        new DriverReportBackCheck(),
        new ConversationSweeperCheck(),
        new ContentFilterCheck(),
        new AgentRoleCheck(),
        new AgentCapCheck(),
        new AgentCostTagCheck(),
        new AgentsEnabledInPublicDemoUiCheck(),
        new UnlimitedModelQuotaCheck(),
    ]);

    public IReadOnlyList<Check> Checks => checks;

    /// <summary>The Findings, the most severe first and by ID within a severity, so the same estate always gives the same list.</summary>
    public IReadOnlyList<Finding> Run(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. checks
                .Select(check => (Check: check, Evidence: check.Examine(estate)))
                .Where(result => result.Evidence.Count > 0)
                .Select(result => new Finding(
                    $"F-{result.Check.Id}",
                    result.Check.Id,
                    result.Check.Title,
                    result.Check.Rule,
                    result.Check.Severity,
                    [.. result.Evidence.Select(evidence => new Evidence(EvidenceMasker.Of(evidence.Subject), EvidenceMasker.Of(evidence.Detail)))],
                    result.Check.AsFoundToggle,
                    result.Check.Fix))
                .OrderByDescending(finding => finding.Severity)
                .ThenBy(finding => finding.Id, StringComparer.Ordinal),
        ];
    }
}
