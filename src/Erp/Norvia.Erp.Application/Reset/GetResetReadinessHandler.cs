using Mediator;

namespace Norvia.Erp.Application.Reset;

public sealed class GetResetReadinessHandler(IResetSwitch setting, IQuietCheck quiet) : IQueryHandler<GetResetReadinessQuery, ResetReadiness>
{
    public async ValueTask<ResetReadiness> Handle(GetResetReadinessQuery query, CancellationToken cancellationToken) =>
        new(setting.IsOn, setting.IsOn ? await quiet.FindBusySubscriptionAsync(cancellationToken) : null);
}
