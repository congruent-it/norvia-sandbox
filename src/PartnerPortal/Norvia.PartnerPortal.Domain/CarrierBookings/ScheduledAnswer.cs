namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>The automatic answer a booking request was received with, and when it is due.</summary>
/// <param name="Mode">Auto-confirm or auto-reject; a manual Persona schedules nothing.</param>
/// <param name="RejectionReason">The reason an auto-reject gives.</param>
public sealed record ScheduledAnswer(CarrierAnswerMode Mode, CarrierRejectionReason RejectionReason, DateTimeOffset DueAt)
{
    public CarrierAnswerMode Mode { get; } = Mode is CarrierAnswerMode.AutoConfirm or CarrierAnswerMode.AutoReject
        ? Mode
        : throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "Only an automatic mode schedules an answer.");
}
