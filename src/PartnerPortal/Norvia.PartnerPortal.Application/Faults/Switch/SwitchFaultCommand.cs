using Mediator;

namespace Norvia.PartnerPortal.Application.Faults.Switch;

/// <summary>Switches a fault on, for <paramref name="Duration"/> after which it expires by itself, or off when the duration is null.</summary>
public sealed record SwitchFaultCommand(string Owner, string Name, TimeSpan? Duration) : ICommand<FaultView>;
