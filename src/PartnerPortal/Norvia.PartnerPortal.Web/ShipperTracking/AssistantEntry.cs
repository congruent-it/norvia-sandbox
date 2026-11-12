using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>One question and what the Shipper was shown for it.</summary>
public sealed record AssistantEntry(string Question, AssistantAnswer Answer);
