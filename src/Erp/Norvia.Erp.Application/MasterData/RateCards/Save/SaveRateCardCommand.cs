using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.RateCards.Save;

/// <summary>
/// Finance creates or edits a Shipper's Rate Card with its whole list of dated versions (E3). Idempotent on content: a repeat of what the
/// card already is changes and publishes nothing.
/// </summary>
public sealed record SaveRateCardCommand(string Id, string ShipperId, IReadOnlyList<RateCardVersionData> Versions) : ICommand<Result<SaveRateCardResult>>;
