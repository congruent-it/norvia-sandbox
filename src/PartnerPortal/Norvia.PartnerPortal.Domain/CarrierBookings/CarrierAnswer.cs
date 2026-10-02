namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>What a Carrier answers to a booking request: it confirms with its own booking reference, or rejects with a reason.</summary>
public abstract record CarrierAnswer
{
    private protected CarrierAnswer()
    {
    }

    /// <summary>Takes the booking. <paramref name="PlannedCollection"/> is when the Carrier plans to collect, if it says.</summary>
    public sealed record Confirmation : CarrierAnswer
    {
        public Confirmation(string bookingReference, DateTimeOffset? plannedCollection = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bookingReference);
            BookingReference = bookingReference.Trim();
            PlannedCollection = plannedCollection;
        }

        /// <summary>The Carrier's own reference for the booking.</summary>
        public string BookingReference { get; }

        public DateTimeOffset? PlannedCollection { get; }
    }

    /// <summary>Turns the booking down. A text is required for <see cref="CarrierRejectionReason.Other"/>.</summary>
    public sealed record Rejection : CarrierAnswer
    {
        public Rejection(CarrierRejectionReason reason, string? text = null)
        {
            if (reason == CarrierRejectionReason.Other)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(text);
            }

            Reason = reason;
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        public CarrierRejectionReason Reason { get; }

        public string? Text { get; }
    }
}
