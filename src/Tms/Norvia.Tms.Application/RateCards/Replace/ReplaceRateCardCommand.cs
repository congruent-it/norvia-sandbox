using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.RateCards.Replace;

/// <summary>
/// Replaces the TMS's copy of a Rate Card, its whole list of dated versions, with the ERP's record (E3). Upserts: a new Rate Card appears.
/// Idempotent on the Master-data revision: a revision that is not higher than the copy's changes nothing. A replaced Rate Card changes
/// only what is rated after it arrives; re-rating is a separate flow.
/// </summary>
public sealed record ReplaceRateCardCommand(string Id, string ShipperId, int Revision, IReadOnlyList<RateCardVersionData> Versions)
    : ICommand<Result<ReplaceRateCardResult>>;
