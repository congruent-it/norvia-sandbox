using System.Globalization;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierInvoices.Draft;

public sealed class GetCarrierInvoiceDraftHandler(
    IReadRepository<CarrierBookingRequest> bookings,
    IReadRepository<CarrierInvoice> invoices,
    IReadRepository<CarrierInvoiceOutcome> outcomes,
    IPersonaDirectory personas,
    TimeProvider time)
    : IQueryHandler<GetCarrierInvoiceDraftQuery, CarrierInvoiceDraftView?>
{
    public async ValueTask<CarrierInvoiceDraftView?> Handle(GetCarrierInvoiceDraftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (personas.Find(query.PersonaId) is not { PartyType: PartyType.Carrier })
        {
            return null;
        }

        var sent = await invoices.ListAsync(new CarrierInvoicesOfPersonaSpec(query.PersonaId), cancellationToken);
        var told = await outcomes.ListAsync(new CarrierInvoiceOutcomesOfPersonaSpec(query.PersonaId), cancellationToken);
        var invoiced = CarrierInvoiced.References(sent, told);

        var delivered = await bookings.ListAsync(new CarrierDeliveriesOfPersonaSpec(query.PersonaId), cancellationToken);
        var lines = delivered
            .Where(request => request.CarrierBookingReference is { } reference && !invoiced.Contains(reference))
            .Select(request => new CarrierInvoiceDraftLine(
                request.CarrierBookingReference!, request.CarrierBookingNumber, request.ShipmentNumber, request.Details.CarrierCost?.Total))
            .ToList();

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return new CarrierInvoiceDraftView(SuggestedNumber(today, sent.Count), today, lines);
    }

    // One a Persona hasn't used: its invoices so far, plus one, in the date's own numbers.
    private static string SuggestedNumber(DateOnly today, int sent) =>
        $"INV-{today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{(sent + 1).ToString("000", CultureInfo.InvariantCulture)}";
}
