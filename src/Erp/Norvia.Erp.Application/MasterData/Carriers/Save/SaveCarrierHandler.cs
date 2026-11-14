using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Carriers.List;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.MasterData.Carriers.Save;

/// <summary>
/// Creates or edits a Carrier. The change and its <c>Carrier</c> event are saved by one save, so a rolled-back edit publishes nothing.
/// </summary>
public sealed partial class SaveCarrierHandler(
    IRepository<Carrier> carriers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SaveCarrierHandler> logger) : ICommandHandler<SaveCarrierCommand, Result<SaveCarrierResult>>
{
    public const int MaxNameLength = 200;
    public const int MaxIdLength = 50;

    public async ValueTask<Result<SaveCarrierResult>> Handle(SaveCarrierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (MasterDataSaveKey.IsTooLong(command.IdempotencyKey))
        {
            return Refuse(MasterDataSaveKey.TooLongMessage, MasterDataSaveKey.TooLongCode);
        }

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name))
        {
            return Refuse("The Carrier needs an ID and a name.", "MasterDataFieldRequired");
        }

        if (command.Id.Length > MaxIdLength || command.Name.Length > MaxNameLength)
        {
            return Refuse($"The Carrier's ID is at most {MaxIdLength} characters and its name {MaxNameLength}.", "MasterDataFieldTooLong");
        }

        var id = new CarrierId(command.Id);
        var existing = await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(id), cancellationToken);
        MasterDataSaveOutcome outcome;
        Carrier carrier;
        if (existing is null)
        {
            carrier = Carrier.CreateByFinance(id, command.Name, command.Active, command.IdempotencyKey);
            await carriers.AddAsync(carrier, cancellationToken);
            outcome = MasterDataSaveOutcome.Created;
        }
        else
        {
            carrier = existing;
            if (carrier.IsRepeat(command.IdempotencyKey))
            {
                LogRepeat(logger, command.Id);
                return new SaveCarrierResult(ViewOf(carrier), MasterDataSaveOutcome.Repeat);
            }

            if (!carrier.Edit(command.Name, command.Active, command.IdempotencyKey))
            {
                LogUnchanged(logger, command.Id);
                return new SaveCarrierResult(ViewOf(carrier), MasterDataSaveOutcome.Unchanged);
            }

            await carriers.UpdateAsync(carrier, cancellationToken);
            outcome = MasterDataSaveOutcome.Updated;
        }

        var view = ViewOf(carrier);
        var correlationId = Guid.NewGuid().ToString();
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.MasterData,
            [TrackingKeys.PartyId] = command.Id,
        });
        outbox.Add(new CarrierV1(Guid.CreateVersion7(), time.GetUtcNow(), correlationId, view));
        LogSaved(logger, command.Id, outcome, view.Revision);
        return new SaveCarrierResult(view, outcome);
    }

    private static CarrierView ViewOf(Carrier carrier) => new(carrier.Id.Value, carrier.Name, carrier.Active, carrier.Revision);

    private static Result<SaveCarrierResult> Refuse(string message, string code) =>
        Result<SaveCarrierResult>.Invalid(new ValidationError("Carrier", message, code, ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier {CarrierId} {Outcome} at Master-data revision {Revision}")]
    private static partial void LogSaved(ILogger logger, string carrierId, MasterDataSaveOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier {CarrierId}: this save was applied before under its Idempotency-Key; nothing changed or published")]
    private static partial void LogRepeat(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier {CarrierId} already has this content; nothing changed or published")]
    private static partial void LogUnchanged(ILogger logger, string carrierId);
}
