namespace Norvia.Tms.Application.Fleet.Planning;

/// <summary>A Trip of a day that Needs a Driver or Needs a Vehicle (P11), which the Capacity view links to.</summary>
public sealed record FlaggedTripView(string Number, bool NeedsDriver, bool NeedsVehicle);
