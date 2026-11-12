namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The one extra call a question needs: a Transport Order number to get, or a Shipper reference to find.</summary>
public sealed record ExtraFetch(ExtraFetchKind Kind, string Value);
