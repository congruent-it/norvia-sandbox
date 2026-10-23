namespace Norvia.ServiceDefaults.Faults;

/// <summary>A fault and whether it is on now.</summary>
/// <param name="On">Whether the fault is on. A fault whose time has run out is off.</param>
/// <param name="Until">When the fault expires by itself, or null while it is off.</param>
/// <param name="RemainingSeconds">How many whole seconds are left until it expires, or null while it is off.</param>
public sealed record FaultStatus(string Name, string Owner, string Description, bool On, DateTimeOffset? Until, int? RemainingSeconds);
