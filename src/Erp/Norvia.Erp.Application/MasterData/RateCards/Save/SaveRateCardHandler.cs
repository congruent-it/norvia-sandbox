using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.MasterData.RateCards.Save;

/// <summary>
/// Creates or edits a Rate Card. The change and its <c>RateCard</c> event are saved by one save, so a rolled-back edit publishes nothing.
/// </summary>
public sealed partial class SaveRateCardHandler(
    IRepository<RateCard> rateCards,
    IReadRepository<Shipper> shippers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SaveRateCardHandler> logger) : ICommandHandler<SaveRateCardCommand, Result<SaveRateCardResult>>
{
    public const int MaxIdLength = 50;

    public async ValueTask<Result<SaveRateCardResult>> Handle(SaveRateCardCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (MasterDataSaveKey.IsTooLong(command.IdempotencyKey))
        {
            return Refuse(MasterDataSaveKey.TooLongMessage, MasterDataSaveKey.TooLongCode);
        }

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.ShipperId))
        {
            return Refuse("The Rate Card needs an ID and a Shipper.", "MasterDataFieldRequired");
        }

        if (command.Id.Length > MaxIdLength || command.ShipperId.Length > MaxIdLength)
        {
            return Refuse($"The Rate Card's ID and its Shipper's ID are at most {MaxIdLength} characters.", "MasterDataFieldTooLong");
        }

        var versions = RateCardData.ToDomain(command.Versions ?? []);

        if (RateCardVersionRules.Problem(versions) is { } problem)
        {
            return Refuse(problem.Message, problem.IsOverlap ? "RateCardVersionsOverlap" : "RateCardVersionInvalid");
        }

        var shipperId = new ShipperId(command.ShipperId);
        if (await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(shipperId), cancellationToken) is null)
        {
            return Refuse($"Shipper {command.ShipperId} is not one of the ERP's Shippers.", "RateCardPartyUnknown");
        }

        var id = new RateCardId(command.Id);
        if ((await rateCards.ListAsync(new RateCardOfShipperSpec(shipperId), cancellationToken)).Any(other => other.Id != id))
        {
            return Refuse($"Shipper {command.ShipperId} already has a Rate Card; a Shipper has one.", "RateCardShipperHasCard");
        }

        var existing = await rateCards.FirstOrDefaultAsync(new RateCardByIdSpec(id), cancellationToken);
        MasterDataSaveOutcome outcome;
        RateCard card;
        if (existing is null)
        {
            card = RateCard.CreateByFinance(id, shipperId, versions, command.IdempotencyKey);
            await rateCards.AddAsync(card, cancellationToken);
            outcome = MasterDataSaveOutcome.Created;
        }
        else
        {
            card = existing;
            if (card.IsRepeat(command.IdempotencyKey))
            {
                LogRepeat(logger, command.Id);
                return new SaveRateCardResult(RecordOf(card), MasterDataSaveOutcome.Repeat);
            }

            if (!card.Edit(shipperId, versions, command.IdempotencyKey))
            {
                LogUnchanged(logger, command.Id);
                return new SaveRateCardResult(RecordOf(card), MasterDataSaveOutcome.Unchanged);
            }

            await rateCards.UpdateAsync(card, cancellationToken);
            outcome = MasterDataSaveOutcome.Updated;
        }

        var record = RecordOf(card);
        var correlationId = Guid.NewGuid().ToString();
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.MasterData,
            [TrackingKeys.PartyId] = command.ShipperId,
        });
        outbox.Add(new RateCardV1(Guid.CreateVersion7(), time.GetUtcNow(), correlationId, record));
        LogSaved(logger, command.Id, outcome, record.Revision);
        return new SaveRateCardResult(record, outcome);
    }

    private static RateCardRecord RecordOf(RateCard card) =>
        new(card.Id.Value, card.ShipperId.Value, card.Revision, RateCardData.Of(card.Versions));

    private static Result<SaveRateCardResult> Refuse(string message, string code) =>
        Result<SaveRateCardResult>.Invalid(new ValidationError("RateCard", message, code, ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate Card {RateCardId} {Outcome} at Master-data revision {Revision}")]
    private static partial void LogSaved(ILogger logger, string rateCardId, MasterDataSaveOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate Card {RateCardId}: this save was applied before under its Idempotency-Key; nothing changed or published")]
    private static partial void LogRepeat(ILogger logger, string rateCardId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate Card {RateCardId} already has this content; nothing changed or published")]
    private static partial void LogUnchanged(ILogger logger, string rateCardId);
}
