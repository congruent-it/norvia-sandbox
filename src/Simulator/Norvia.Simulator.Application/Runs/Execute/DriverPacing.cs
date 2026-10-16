namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// How long the Drivers wait for the estate to catch up: a dispatched Trip reaches the Driver app through the Trip dispatch Logic App, and what a Driver
/// reports is recorded at the TMS through the Driver reports Logic App, so the Driver app shows the Trip started or completed a little later. A Driver
/// asks every <see cref="PollInterval"/>, up to <see cref="PollAttempts"/> times.
/// </summary>
public sealed record DriverPacing(TimeSpan PollInterval, int PollAttempts);
