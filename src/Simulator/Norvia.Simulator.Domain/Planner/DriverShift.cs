namespace Norvia.Simulator.Domain.Planner;

/// <summary>A Driver who works a day and the hours of their Shift, as the TMS reports them (P10).</summary>
public sealed record DriverShift(string DriverId, TimeOnly Start, TimeOnly End);
