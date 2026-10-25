using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>A queue or subscription holds dead-lettered messages and has no alert on its dead-letter queue, so nobody was told.</summary>
public sealed class UntoldDeadLettersCheck : UntoldFailureCheck
{
    public const string CheckId = "DLQ-UNTOLD";

    public override string Id => CheckId;

    public override string Rule => "A queue or subscription that holds dead-lettered messages has an alert on its dead-letter queue, so somebody is told.";

    public override string Title => "A dead-letter queue holds messages and nobody was told";

    public override string AsFoundToggle => "entitiesWithoutDeadLetterAlert";

    public override string Fix =>
        "Remove the entity from asFound.entitiesWithoutDeadLetterAlert and provision the estate: infra/estate/alerts.bicep then builds alert-dead-letter-<entity>. Then look at the dead-lettered messages in the Integration console.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. estate.ServiceBusEntities
                .Where(entity => !entity.HasDeadLetterAlert && entity.DeadLetteredMessages > 0)
                .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .Select(entity => new Evidence(
                    entity.Name,
                    $"The dead-letter queue of {entity.Name} holds {entity.DeadLetteredMessages} {(entity.DeadLetteredMessages == 1 ? "message" : "messages")} and no alert watches it.")),
        ];
    }
}
