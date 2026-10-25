using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Application Insights keeps what it traces at least as long as the Logic Apps keep their run history.</summary>
public sealed class AppInsightsRetentionCheck : Check
{
    public const string CheckId = "APPINSIGHTS-RETENTION";

    public override string Id => CheckId;

    public override string Rule => "Application Insights keeps what it traces at least as long as the Logic Apps keep their run history.";

    public override string Title => "Application Insights keeps less than the run history";

    public override Severity Severity => Severity.Medium;

    public override string AsFoundToggle => "appInsightsRetentionShort";

    public override string Fix =>
        "Set appInsightsRetentionShort to false and provision the estate: infra/estate/monitoring.bicep then sets the retention to integrationRetentionDays.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        var retention = estate.Retention;
        return retention is not null && retention.AppInsightsDays < retention.RunHistoryDays
            ? [new Evidence("Application Insights", $"It keeps {retention.AppInsightsDays} days and the run history {retention.RunHistoryDays}, so run bodies outlive the index that finds them.")]
            : [];
    }
}
