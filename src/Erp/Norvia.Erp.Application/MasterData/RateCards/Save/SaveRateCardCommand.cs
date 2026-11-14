using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.RateCards.Save;

/// <summary>
/// Finance creates or edits a Shipper's Rate Card with its whole list of dated versions (E3). Idempotent on content, and on the sender's key when it sends one: a repeat of what the
/// card already is, or of a save already applied under the same key, changes and publishes nothing.
/// </summary>
public sealed record SaveRateCardCommand(string Id, string ShipperId, IReadOnlyList<RateCardVersionData> Versions, string? IdempotencyKey = null) : ICommand<Result<SaveRateCardResult>>;
