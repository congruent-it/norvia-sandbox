using Norvia.Simulator.Domain.Invoicing;

namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>
/// The invoicing stage of the funnel, for the Shippers whose bookings Norvia is expected to accept (<see cref="Shippers"/>). It is the most the stage can reach:
/// a Shipper gets an invoice only when a Transport Order of it was delivered, which is known only when the run is over.
/// </summary>
public sealed record InvoicingPreview(InvoicingShares Shares, InvoicingFunnelCounts Counts)
{
    public int Shippers => Counts.Shippers;
}
