using Ardalis.Specification;

namespace Norvia.Erp.Domain.BillableLines;

/// <summary>Every Waiting Billable line, the earliest delivery first, for the lists Finance reads.</summary>
public sealed class WaitingBillableLinesSpec : Specification<BillableLine>
{
    public WaitingBillableLinesSpec() =>
        Query.AsNoTracking().Where(line => line.Status == BillableLineStatus.Waiting).OrderBy(line => line.DeliveryTime).ThenBy(line => line.Id);
}
