namespace Norvia.Tms.Api.Exceptions;

/// <summary>The Planner's message to the Shipper, starting from the template for the Exception's type, and who sent it.</summary>
/// <param name="Message">Required: what the Shipper is told.</param>
/// <param name="Planner">Required: the Planner's name, as the TMS UI names them.</param>
/// <param name="AgentName">Optional, with <paramref name="AgentRunId"/>: the Agent whose draft the message was sent from, edited or not, which the notification's trace then names.</param>
/// <param name="AgentRunId">Optional, with <paramref name="AgentName"/>: that Agent's run.</param>
public sealed record NotifyShipperRequest(string? Message, string? Planner, string? AgentName = null, string? AgentRunId = null);
