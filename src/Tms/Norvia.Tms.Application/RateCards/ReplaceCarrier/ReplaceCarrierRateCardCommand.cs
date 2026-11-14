using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.RateCards.ReplaceCarrier;

/// <summary>
/// Replaces the TMS's copy of a Carrier Rate Card, its whole list of dated versions, with the ERP's record (E3). Upserts: a new card appears.
/// Idempotent on the Master-data revision: a revision that is not higher than the copy's changes nothing.
/// </summary>
public sealed record ReplaceCarrierRateCardCommand(string Id, string CarrierId, int Revision, IReadOnlyList<RateCardVersionData> Versions)
    : ICommand<Result<ReplaceCarrierRateCardResult>>;
