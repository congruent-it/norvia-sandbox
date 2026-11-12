namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>What the Shipper tracking assistant's Agent returned (ADR 0049): the answer text, the input field behind each claim, and the tokens the run used.</summary>
/// <param name="NoInformation">The Agent says the data holds nothing that answers the question. Such a reply needs no claim.</param>
public sealed record AssistantReply(string Answer, IReadOnlyList<AssistantClaim> Claims, bool NoInformation, int TokensUsed);

/// <summary>A claim of the answer and where in the tracking data it came from.</summary>
/// <param name="Field">A path into the tracking data, such as <c>detail.eta</c> or <c>orders[2].status</c>.</param>
public sealed record AssistantClaim(string Field);
