using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>Every queue and subscription with a dead-letter queue has an alert on it. Without one, nobody is told a message was dead-lettered.</summary>
public sealed class DeadLetterAlertCheck : Check
{
    public const string CheckId = "DLQ-ALERT";

    public override string Id => CheckId;

    public override string Rule => "Every Service Bus queue and subscription with a dead-letter queue has an alert on it.";

    public override string Title => "A dead-letter queue has no alert";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "entitiesWithoutDeadLetterAlert";

    public override string Fix =>
        "Remove the entity from asFound.entitiesWithoutDeadLetterAlert (infra/presets/fixed.json has it empty) and provision the estate: infra/estate/alerts.bicep then builds alert-dead-letter-<entity>.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. estate.ServiceBusEntities
                .Where(entity => !entity.HasDeadLetterAlert)
                .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .Select(entity => new Evidence(entity.Name, $"No alert watches the dead-letter queue of {entity.Name}.")),
        ];
    }
}
