using Mediator;

namespace Norvia.PartnerPortal.Application.DriverAppFault.Switch;

/// <summary>Switches the "Driver app unavailable" fault on or off.</summary>
public sealed record SetDriverAppFaultCommand(bool Unavailable) : ICommand<DriverAppFaultView>;
