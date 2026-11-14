namespace Norvia.Erp.Domain.RateCards;

/// <summary>Why a list of dated versions can't be a card's versions.</summary>
/// <param name="IsOverlap">True when two versions overlap in time, which the sync refuses under a code of its own.</param>
public sealed record RateCardVersionProblem(string Message, bool IsOverlap = false);
