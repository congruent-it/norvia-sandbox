namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>One step of the progress so far. <paramref name="Time"/> is local to <paramref name="Place"/>, <c>dd-MM-yyyy HH:mm</c>, as returned.</summary>
public sealed record ShipperProgressEvent(ShipperProgressType Type, string Time, ShipperPlace Place);
