namespace Norvia.Simulator.Application.Ports;

/// <summary>An Open Exception of the TMS's list (P7), as the Planner needs to find it: its number, its Shipment and its type.</summary>
public sealed record OpenExceptionSummary(string ExceptionNumber, string ShipmentNumber, string Type);
