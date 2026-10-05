namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>Stands in for the tracking queue: keeps every message put on it, or fails as Service Bus would when unreachable.</summary>
internal sealed class StubTrackingQueue : ITrackingQueue
{
    public List<TrackingMessage> Messages { get; } = [];

    public Exception? Failure { get; set; }

    public Task EnqueueAsync(TrackingMessage message, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Messages.Add(message);
        return Task.CompletedTask;
    }
}
