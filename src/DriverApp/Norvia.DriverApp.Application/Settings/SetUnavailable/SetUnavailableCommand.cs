using Mediator;

namespace Norvia.DriverApp.Application.Settings.SetUnavailable;

/// <summary>Switches the "Driver app unavailable" demo fault on or off.</summary>
public sealed record SetUnavailableCommand(bool Unavailable) : ICommand;
