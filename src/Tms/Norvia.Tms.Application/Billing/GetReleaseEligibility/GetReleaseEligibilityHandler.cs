using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing.GetReleaseEligibility;

public sealed class GetReleaseEligibilityHandler(IReadRepository<TransportOrder> transportOrders, ReleaseEligibilityCheck eligibility)
    : IQueryHandler<GetReleaseEligibilityQuery, Result<ReleaseEligibility>>
{
    public async ValueTask<Result<ReleaseEligibility>> Handle(GetReleaseEligibilityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            && await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is { BillingState: not null } order
                ? await eligibility.CheckAsync(order, cancellationToken)
                : Result.NotFound();
    }
}
