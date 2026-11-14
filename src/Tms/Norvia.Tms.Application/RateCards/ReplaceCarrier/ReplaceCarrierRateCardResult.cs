namespace Norvia.Tms.Application.RateCards.ReplaceCarrier;

/// <param name="Applied">False when the copy already had this revision or a later one.</param>
/// <param name="Revision">The revision the copy holds now.</param>
public sealed record ReplaceCarrierRateCardResult(bool Applied, int Revision);
