using Ardalis.Result;
using Mediator;

namespace Norvia.PartnerPortal.Application.Reset;

public sealed class ResetEstateHandler(IResetSwitch setting, ITransactionalData data) : ICommandHandler<ResetEstateCommand, Result<ResetReport>>
{
    public const string DisabledCode = "ResetDisabled";

    public async ValueTask<Result<ResetReport>> Handle(ResetEstateCommand command, CancellationToken cancellationToken) =>
        setting.IsOn
            ? new ResetReport(await data.ClearAsync(cancellationToken))
            : Result.Forbidden("The reset is switched off for the Partner portal.");
}
