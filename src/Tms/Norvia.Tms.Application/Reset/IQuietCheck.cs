namespace Norvia.Tms.Application.Reset;

/// <summary>Asks whether the topic subscriptions that feed records back into the estate are empty. A reset never purges them: a late message would re-create a record after the wipe.</summary>
public interface IQuietCheck
{
    /// <summary>The first subscription holding unprocessed messages, as <c>topic/subscription</c>, or null when all are quiet.</summary>
    Task<string?> FindBusySubscriptionAsync(CancellationToken cancellationToken);
}
