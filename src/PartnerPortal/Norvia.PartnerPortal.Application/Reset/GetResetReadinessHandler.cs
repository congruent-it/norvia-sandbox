using Mediator;

namespace Norvia.PartnerPortal.Application.Reset;

public sealed class GetResetReadinessHandler(IResetSwitch setting) : IQueryHandler<GetResetReadinessQuery, ResetReadiness>
{
    public ValueTask<ResetReadiness> Handle(GetResetReadinessQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ResetReadiness(setting.IsOn, BusySubscription: null));
}
