using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.List;
using Norvia.PartnerPortal.Application.CarrierInvoices.Outcomes;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierInvoices;

/// <summary>
/// Reads a Carrier Persona's side of the portal, so the Simulator can play the Carrier's invoicing (E2): the price Norvia offered for each booking in its inbox, which
/// is what the Carrier invoices, and the outcomes of its Carrier invoices, which is what its invoices page shows. They are the GETs of the booking and invoice outcome
/// endpoints: the same addresses and the same credential, the Carrier's own key. They change nothing.
/// </summary>
public static class CarrierInboxEndpoints
{
    public static IEndpointRouteBuilder MapCarrierInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/carrier/v1/{carrierId}/bookings", ListBookingsAsync).AllowAnonymous();
        endpoints.MapGet(CarrierInvoiceOutcomeEndpoint.Route, ListOutcomesAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<IReadOnlyList<CarrierInboxBooking>>, UnauthorizedHttpResult, NotFound>> ListBookingsAsync(
        string carrierId,
        [FromHeader(Name = EndpointCredential.ApiKeyHeader)] string? apiKey,
        IMediator mediator,
        IPersonaDirectory personas,
        IOptions<CarrierEndpointOptions> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(personas);
        ArgumentNullException.ThrowIfNull(options);

        if (Check(carrierId, apiKey, personas, options.Value) is { } refused)
        {
            return refused ? TypedResults.Unauthorized() : TypedResults.NotFound();
        }

        var carrier = personas.All.First(persona => persona.PartyType == PartyType.Carrier && persona.PartyId == carrierId);
        var requests = await mediator.Send(new ListCarrierBookingRequestsQuery(carrier.Id), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CarrierInboxBooking>>([.. requests.Select(CarrierInboxBooking.From)]);
    }

    public static async Task<Results<Ok<IReadOnlyList<CarrierInboxOutcome>>, UnauthorizedHttpResult, NotFound>> ListOutcomesAsync(
        string carrierId,
        [FromHeader(Name = EndpointCredential.ApiKeyHeader)] string? apiKey,
        IMediator mediator,
        IPersonaDirectory personas,
        IOptions<CarrierEndpointOptions> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(personas);
        ArgumentNullException.ThrowIfNull(options);

        if (Check(carrierId, apiKey, personas, options.Value) is { } refused)
        {
            return refused ? TypedResults.Unauthorized() : TypedResults.NotFound();
        }

        var carrier = personas.All.First(persona => persona.PartyType == PartyType.Carrier && persona.PartyId == carrierId);
        var outcomes = await mediator.Send(new ListCarrierInvoiceOutcomesQuery(carrier.Id), cancellationToken);
        return TypedResults.Ok<IReadOnlyList<CarrierInboxOutcome>>([.. outcomes.OrderByDescending(outcome => outcome.ReceivedAt).Select(CarrierInboxOutcome.From)]);
    }

    // Who the caller is comes first, so an unknown Carrier and a wrong credential look the same from outside. True: unauthorized. False: a keyed Carrier without a Persona. Null: fine.
    private static bool? Check(string carrierId, string? apiKey, IPersonaDirectory personas, CarrierEndpointOptions options)
    {
        if (!EndpointCredential.IsOf(carrierId, apiKey, options.ApiKeys))
        {
            return true;
        }

        return personas.All.Any(persona => persona.PartyType == PartyType.Carrier && persona.PartyId == carrierId) ? null : false;
    }
}

/// <summary>A booking in the Carrier's inbox as the endpoint lists it: the booking and what Norvia offered for it, which the Carrier invoices.</summary>
/// <param name="AgreedAmount">The price Norvia offered, in EUR without VAT; null for a request that carried none.</param>
public sealed record CarrierInboxBooking(string CarrierBookingNumber, string ShipmentNumber, string? CarrierBookingReference, string Status, decimal? AgreedAmount)
{
    public static CarrierInboxBooking From(CarrierBookingRequestView request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request.CarrierBookingNumber, request.ShipmentNumber, request.CarrierBookingReference, request.Status.ToString(), request.Details.CarrierCost?.Total);
    }
}

/// <summary>An outcome on the Carrier's invoices page as the endpoint lists it, in the Carrier invoice outcome format.</summary>
public sealed record CarrierInboxOutcome(string InvoiceNo, string CorrelationId, DateTimeOffset ReceivedAt, decimal ApprovedAmount, IReadOnlyList<CarrierInboxOutcomeLine> Lines)
{
    public static CarrierInboxOutcome From(CarrierInvoiceOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new(
            outcome.InvoiceNumber, outcome.CorrelationId, outcome.ReceivedAt, outcome.ApprovedAmount,
            [.. outcome.Lines.Select(line => new CarrierInboxOutcomeLine(line.BookingRef, CarrierInvoiceOutcomeResults.WireNameOf(line.Result), line.AcceptedAmount, line.Remark))]);
    }
}

public sealed record CarrierInboxOutcomeLine(string BookingRef, string Result, decimal AcceptedAmount, string? Remark);
