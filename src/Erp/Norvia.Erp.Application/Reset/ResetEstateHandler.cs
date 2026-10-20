using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.Reset;

public sealed class ResetEstateHandler(IResetSwitch setting, IQuietCheck quiet, ITransactionalData data)
    : ICommandHandler<ResetEstateCommand, Result<ResetReport>>
{
    public const string DisabledCode = "ResetDisabled";
    public const string SubscriptionBusyCode = "SubscriptionBusy";

    public async ValueTask<Result<ResetReport>> Handle(ResetEstateCommand command, CancellationToken cancellationToken)
    {
        if (!setting.IsOn)
        {
            return Result.Forbidden("The reset is switched off for the ERP.");
        }

        if (await quiet.FindBusySubscriptionAsync(cancellationToken) is { } busy)
        {
            return Result.Conflict($"The subscription {busy} holds unprocessed messages. Try again when it is empty.");
        }

        return new ResetReport(await data.ClearAsync(cancellationToken));
    }
}
