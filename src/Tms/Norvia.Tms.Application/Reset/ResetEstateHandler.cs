using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.ProofsOfDelivery;

namespace Norvia.Tms.Application.Reset;

public sealed class ResetEstateHandler(IResetSwitch setting, IQuietCheck quiet, ITransactionalData data, IProofOfDeliveryFiles files)
    : ICommandHandler<ResetEstateCommand, Result<ResetReport>>
{
    public const string DisabledCode = "ResetDisabled";
    public const string SubscriptionBusyCode = "SubscriptionBusy";

    public async ValueTask<Result<ResetReport>> Handle(ResetEstateCommand command, CancellationToken cancellationToken)
    {
        if (!setting.IsOn)
        {
            return Result.Forbidden("The reset is switched off for the TMS.");
        }

        if (await quiet.FindBusySubscriptionAsync(cancellationToken) is { } busy)
        {
            return Result.Conflict($"The subscription {busy} holds unprocessed messages. Try again when it is empty.");
        }

        // The records go first: a file left behind is harmless, a record pointing at a deleted file is not.
        var deleted = new List<DeletedKind>(await data.ClearAsync(cancellationToken));
        deleted.Add(new DeletedKind("Proof of delivery files", await files.DeleteAllAsync(cancellationToken)));
        return new ResetReport(deleted);
    }
}
