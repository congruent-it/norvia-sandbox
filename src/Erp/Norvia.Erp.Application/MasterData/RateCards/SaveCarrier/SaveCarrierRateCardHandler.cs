using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.MasterData.RateCards.SaveCarrier;

/// <summary>
/// Creates or edits a Carrier Rate Card. The change and its <c>CarrierRateCard</c> event are saved by one save, so a rolled-back edit publishes nothing.
/// </summary>
public sealed partial class SaveCarrierRateCardHandler(
    IRepository<CarrierRateCard> rateCards,
    IReadRepository<Carrier> carriers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SaveCarrierRateCardHandler> logger) : ICommandHandler<SaveCarrierRateCardCommand, Result<SaveCarrierRateCardResult>>
{
    public const int MaxIdLength = 50;

    public async ValueTask<Result<SaveCarrierRateCardResult>> Handle(SaveCarrierRateCardCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.CarrierId))
        {
            return Refuse("The Carrier Rate Card needs an ID and a Carrier.", "MasterDataFieldRequired");
        }

        if (command.Id.Length > MaxIdLength || command.CarrierId.Length > MaxIdLength)
        {
            return Refuse($"The Carrier Rate Card's ID and its Carrier's ID are at most {MaxIdLength} characters.", "MasterDataFieldTooLong");
        }

        var versions = RateCardData.ToDomain(command.Versions ?? []);

        if (RateCardVersionRules.Problem(versions) is { } problem)
        {
            return Refuse(problem, problem.Contains("overlap", StringComparison.Ordinal) ? "RateCardVersionsOverlap" : "RateCardVersionInvalid");
        }

        var carrierId = new CarrierId(command.CarrierId);
        if (await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(carrierId), cancellationToken) is null)
        {
            return Refuse($"Carrier {command.CarrierId} is not one of the ERP's Carriers.", "RateCardPartyUnknown");
        }

        var id = new RateCardId(command.Id);
        if ((await rateCards.ListAsync(new CarrierRateCardOfCarrierSpec(carrierId), cancellationToken)).Any(other => other.Id != id))
        {
            return Refuse($"Carrier {command.CarrierId} already has a Carrier Rate Card; a Carrier has one.", "RateCardCarrierHasCard");
        }

        var existing = await rateCards.FirstOrDefaultAsync(new CarrierRateCardByIdSpec(id), cancellationToken);
        MasterDataSaveOutcome outcome;
        CarrierRateCard card;
        if (existing is null)
        {
            card = CarrierRateCard.CreateByFinance(id, carrierId, versions);
            await rateCards.AddAsync(card, cancellationToken);
            outcome = MasterDataSaveOutcome.Created;
        }
        else
        {
            card = existing;
            if (!card.Edit(carrierId, versions))
            {
                LogUnchanged(logger, command.Id);
                return new SaveCarrierRateCardResult(RecordOf(card), MasterDataSaveOutcome.Unchanged);
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
            [TrackingKeys.PartyId] = command.CarrierId,
        });
        outbox.Add(new CarrierRateCardV1(Guid.CreateVersion7(), time.GetUtcNow(), correlationId, record));
        LogSaved(logger, command.Id, outcome, record.Revision);
        return new SaveCarrierRateCardResult(record, outcome);
    }

    private static CarrierRateCardRecord RecordOf(CarrierRateCard card) =>
        new(card.Id.Value, card.CarrierId.Value, card.Revision, RateCardData.Of(card.Versions));

    private static Result<SaveCarrierRateCardResult> Refuse(string message, string code) =>
        Result<SaveCarrierRateCardResult>.Invalid(new ValidationError("CarrierRateCard", message, code, ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier Rate Card {RateCardId} {Outcome} at Master-data revision {Revision}")]
    private static partial void LogSaved(ILogger logger, string rateCardId, MasterDataSaveOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier Rate Card {RateCardId} already has this content; nothing changed or published")]
    private static partial void LogUnchanged(ILogger logger, string rateCardId);
}
