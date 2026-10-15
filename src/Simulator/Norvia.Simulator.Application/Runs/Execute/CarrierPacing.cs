namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// How long the Planner waits for the Exceptions the Carriers' tracking opens. Norvia processes a tracking message after it has accepted
/// it, so an Exception shows up on the TMS's list a little later: the Planner asks every <see cref="ExceptionPollInterval"/>, up to
/// <see cref="ExceptionPollAttempts"/> times.
/// </summary>
public sealed record CarrierPacing(TimeSpan ExceptionPollInterval, int ExceptionPollAttempts);
