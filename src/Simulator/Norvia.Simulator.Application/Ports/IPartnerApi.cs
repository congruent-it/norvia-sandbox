using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The Norvia Partner API, the only door the Simulator books and reports through (ADR 0001). A booking is sent as its Shipper's
/// Persona, and a Carrier answers, tracks and proves delivery as its Carrier Persona, each with the Persona's token and
/// subscription key. Norvia unreachable is an answer, not an error: <see cref="Domain.Log.LogOutcome.NotReceived"/> for a
/// booking, <see cref="Domain.Log.LogOutcome.CarrierNotReceived"/> for a Carrier's call.
/// </summary>
public interface IPartnerApi
{
    Task<BookingAnswer> SendBookingAsync(PlannedBooking booking, CancellationToken cancellationToken);

    /// <summary>The Carrier confirms or declines a Carrier booking (P4).</summary>
    Task<CarrierCallAnswer> AnswerBookingAsync(SimulatedCarrier carrier, CarrierAnswerRequest answer, CancellationToken cancellationToken);

    /// <summary>The Carrier reports a tracking event (P5), which Norvia accepts at once and processes later.</summary>
    Task<CarrierCallAnswer> SendTrackingAsync(SimulatedCarrier carrier, CarrierTrackingRequest tracking, CancellationToken cancellationToken);

    /// <summary>The Carrier proves a delivery (P6), by claim check: Norvia accepts it at once and processes it later.</summary>
    Task<CarrierCallAnswer> SendProofOfDeliveryAsync(SimulatedCarrier carrier, CarrierProofRequest proof, CancellationToken cancellationToken);

    /// <summary>The Carrier sends its Carrier invoice (E2), which Norvia matches line by line and answers at once.</summary>
    Task<CarrierInvoiceAnswer> SendCarrierInvoiceAsync(SimulatedCarrier carrier, CarrierInvoiceRequest invoice, CancellationToken cancellationToken);
}
