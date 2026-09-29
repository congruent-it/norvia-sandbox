namespace Norvia.Tms.Application.CarrierBookings.Answer;

/// <summary>What a Carrier's answer command names for tracking (coding guidelines §7): the booking, the Carrier and its own trail.</summary>
public interface ICarrierBookingAnswerCommand
{
    string CarrierBookingNumber { get; }

    string CarrierId { get; }

    string CorrelationId { get; }
}
