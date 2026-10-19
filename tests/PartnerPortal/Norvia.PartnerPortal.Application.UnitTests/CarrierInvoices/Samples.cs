using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

internal static class CarrierInvoiceSamples
{
    public static PersonaId Persona => UnitTests.Samples.VanDijkTransport.Id;

    /// <summary>A Confirmed booking, reported Delivered when <paramref name="delivered"/>, with the agreed price of the request.</summary>
    public static CarrierBookingRequest Booking(string number, string reference, bool delivered = true, decimal? price = 100m, PersonaId? persona = null, bool confirmed = true)
    {
        var cost = price is { } total ? new CarrierCost("EUR", total, [new CarrierChargeLine("Freight", total)], IsSpot: false) : null;
        var request = CarrierBookingRequest.Receive(
            persona ?? Persona, number, $"SH-{number[3..]}", "release-1", UnitTests.Samples.Now,
            new CarrierBookingDetails(new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Utrecht", "NLD", null, null), null, [], cost));
        if (confirmed)
        {
            request.Answer(new CarrierAnswer.Confirmation(reference), "answer-1", UnitTests.Samples.Now);
        }

        if (delivered)
        {
            request.ReportTracking(TrackingEventType.Delivered);
        }

        return request;
    }

    public static CarrierInvoice Invoice(string number, params string[] references) => CarrierInvoice.Receive(
        Persona, number, new DateOnly(2026, 9, 24), "corr-1", UnitTests.Samples.Now, CarrierInvoiceStatus.Received, approvedAmount: null,
        [.. references.Select(reference => new CarrierInvoiceLine(reference, 100m, null, CarrierInvoiceLineResult.PriceMismatch))]);

    public static CarrierInvoiceOutcome Outcome(string number, params (string Reference, CarrierInvoiceOutcomeResult Result)[] lines) => CarrierInvoiceOutcome.Receive(
        Persona, number, "corr-2", UnitTests.Samples.Now, 100m,
        [.. lines.Select(line => new CarrierInvoiceOutcomeLine(line.Reference, line.Result, line.Result == CarrierInvoiceOutcomeResult.Rejected ? 0m : 100m, null))]);
}
