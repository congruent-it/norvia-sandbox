namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>A Shipper that booked in the run, by its ID in the ERP and the Persona that receives its invoices.</summary>
internal sealed record InvoicedShipper(string ShipperId, string PersonaName);
