using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperInvoices;
using Norvia.PartnerPortal.Application.ShipperInvoices.List;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.ShipperInvoices;

/// <summary>
/// Reads a Shipper Persona's invoices inbox, so the Simulator can see what reached it. It is the invoice endpoint's GET: the same address and
/// the same credential, the Shipper's own key. It lists what the inbox holds, the latest first, and changes nothing.
/// </summary>
public static class ShipperInboxEndpoint
{
    public static IEndpointRouteBuilder MapShipperInboxEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ShipperInvoiceEndpoint.Route, ListAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<IReadOnlyList<ShipperInboxDocument>>, UnauthorizedHttpResult, NotFound>> ListAsync(
        string shipperId,
        [FromHeader(Name = ShipperInvoiceEndpoint.ApiKeyHeader)] string? apiKey,
        IMediator mediator,
        IPersonaDirectory personas,
        IOptions<ShipperEndpointOptions> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(personas);
        ArgumentNullException.ThrowIfNull(options);

        // Who the caller is comes first, so an unknown Shipper and a wrong credential look the same from outside.
        if (!EndpointCredential.IsOf(shipperId, apiKey, options.Value.ApiKeys))
        {
            return TypedResults.Unauthorized();
        }

        if (personas.All.FirstOrDefault(persona => persona.PartyType == PartyType.Shipper && persona.PartyId == shipperId) is not { } shipper)
        {
            return TypedResults.NotFound();
        }

        var inbox = await mediator.Send(new ListShipperInvoicesQuery(shipper.Id), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<ShipperInboxDocument>>([.. inbox.Select(ShipperInboxDocument.From)]);
    }
}

/// <summary>A document in the inbox as the endpoint lists it: what the Shipper's own system would read from the invoice.</summary>
/// <param name="CreditedInvoiceNumber">The invoice a Credit note corrects; null on an invoice.</param>
/// <param name="CreditedTransportOrderNumber">The line a Credit note corrects; null on an invoice.</param>
public sealed record ShipperInboxDocument(
    string DocumentNumber,
    string DocumentType,
    DateTimeOffset ReceivedAt,
    string? CreditedInvoiceNumber,
    string? CreditedTransportOrderNumber,
    string? Reason,
    IReadOnlyList<string> TransportOrderNumbers,
    decimal TotalWithoutVat,
    decimal Vat,
    decimal TotalWithVat,
    string CorrelationId)
{
    public static ShipperInboxDocument From(ShipperInvoiceView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new(
            view.DocumentNumber, InvoiceDocumentTypes.WireNameOf(view.DocumentType), view.ReceivedAt, view.Credit?.InvoiceNumber, view.Credit?.TransportOrderNumber,
            view.Credit?.Reason, [.. view.Lines.Select(line => line.TransportOrderNumber)], view.TotalWithoutVat, view.Vat, view.TotalWithVat, view.CorrelationId);
    }
}
