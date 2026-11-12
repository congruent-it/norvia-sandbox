using System.Text.Json.Nodes;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The model's whole input: the delimited text it is sent, and the tracking data inside it that the output check reads.</summary>
public sealed record AssistantInput(string Text, JsonNode Data, string DataText, string NowText);
