namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>What a question needs beyond the open order and the first page (ADR 0049): at most one more call, for a number or reference it names that neither holds.</summary>
public enum ExtraFetchKind
{
    GetNumber,
    FindReference,
}
