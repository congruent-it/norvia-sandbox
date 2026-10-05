namespace Norvia.Integration.LocalPartnerApi;

/// <summary>A message for the tracking queue: the Carrier's tracking message as sent, and the application properties API Management sets.</summary>
internal sealed record TrackingMessage(string Body, string MessageId, IReadOnlyDictionary<string, string> Properties);

/// <summary>The tracking queue the Carrier-tracking Logic App reads (ADR 0008).</summary>
internal interface ITrackingQueue
{
    Task EnqueueAsync(TrackingMessage message, CancellationToken cancellationToken);
}
