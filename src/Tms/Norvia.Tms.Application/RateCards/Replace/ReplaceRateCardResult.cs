namespace Norvia.Tms.Application.RateCards.Replace;

/// <param name="Applied">False when the copy already had this revision or a later one.</param>
/// <param name="Revision">The revision the copy holds now.</param>
public sealed record ReplaceRateCardResult(bool Applied, int Revision);
