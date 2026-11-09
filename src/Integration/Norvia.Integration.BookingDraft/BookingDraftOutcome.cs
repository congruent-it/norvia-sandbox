namespace Norvia.Integration.BookingDraft;

/// <summary>The draft, or the failure code it is refused with (<see cref="BookingDraftFailureCodes"/>).</summary>
public sealed record BookingDraftOutcome(string RunId, BookingDraft? Draft, string? FailureCode);
