namespace Norvia.Integration.BookingDraft;

/// <summary>A field the text does not give or gives ambiguously (<paramref name="Ambiguous"/>). <paramref name="Field"/> is named as in the quotes: <c>pickupFrom</c>, <c>items.0.weight</c>.</summary>
public sealed record MissingField(string Field, string Reason, bool Ambiguous = false);
