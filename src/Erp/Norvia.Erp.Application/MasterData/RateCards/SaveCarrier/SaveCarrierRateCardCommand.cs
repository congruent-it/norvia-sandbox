using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.RateCards.SaveCarrier;

/// <summary>
/// Finance creates or edits a Carrier's Carrier Rate Card with its whole list of dated versions (E3). Idempotent on content: a repeat of
/// what the card already is changes and publishes nothing.
/// </summary>
public sealed record SaveCarrierRateCardCommand(string Id, string CarrierId, IReadOnlyList<RateCardVersionData> Versions, string? IdempotencyKey = null)
    : ICommand<Result<SaveCarrierRateCardResult>>;
