namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>Why the TMS refused a Tracking event: a stable code and a readable message.</summary>
public sealed record TrackingEventRefusal(TrackingEventRefusalCode Code, string Message);
