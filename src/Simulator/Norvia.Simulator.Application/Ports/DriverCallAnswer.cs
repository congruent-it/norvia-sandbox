using Norvia.Simulator.Domain.Log;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The Driver app's answer to one of a Driver's calls, as the log shows it. <see cref="Detail"/> holds a refusal's code, never the text the Driver app
/// words it with. A report the Driver app takes is not yet recorded at the TMS: <see cref="CorrelationId"/> is the trail it travels on.
/// </summary>
public sealed record DriverCallAnswer(LogOutcome Outcome, string? CorrelationId, string? Detail);
