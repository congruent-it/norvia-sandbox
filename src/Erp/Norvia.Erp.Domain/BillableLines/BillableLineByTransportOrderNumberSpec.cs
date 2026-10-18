using Ardalis.Specification;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>The Billable line of a Transport Order, which makes recording idempotent on the Transport Order number.</summary>
public sealed class BillableLineByTransportOrderNumberSpec : Specification<BillableLine>
{
    public BillableLineByTransportOrderNumberSpec(string transportOrderNumber) =>
        Query.Where(line => line.Id == new BillableLineId(transportOrderNumber));
}
