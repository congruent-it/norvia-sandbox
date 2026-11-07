namespace Norvia.Tms.Api.TransportOrders;

/// <summary>How Customer service releases a Transport Order without a Proof of delivery: the remark that is required, and who releases it.</summary>
/// <param name="Remark">Required: why, for example "CMR received by post, scanned in the dossier".</param>
/// <param name="ReleasedBy">Required: the Customer service employee who releases it.</param>
/// <param name="AgentName">Optional, with <paramref name="AgentRunId"/>: the Agent whose Suggestion the employee acted on, which the release's trace then names.</param>
/// <param name="AgentRunId">Optional, with <paramref name="AgentName"/>: that Agent's run.</param>
public sealed record ReleaseWithoutProofOfDeliveryRequest(string? Remark, string? ReleasedBy, string? AgentName = null, string? AgentRunId = null);
