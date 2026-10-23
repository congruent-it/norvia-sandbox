namespace Norvia.ServiceDefaults.Faults;

/// <summary>A runtime fault an application can be made to have, so a demo can show how the estate finds it.</summary>
/// <param name="Name">The fault's stable name, such as <c>driver-app-unavailable</c>. It names the fault on the presenter panel and on the hop it caused.</param>
/// <param name="Owner">The application the fault breaks, such as <c>driver-app</c>.</param>
/// <param name="Description">What the fault does, in a sentence the presenter panel shows.</param>
public sealed record FaultDefinition(string Name, string Owner, string Description);
