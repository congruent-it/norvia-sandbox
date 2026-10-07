using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierBookings.Receive;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>
/// A Carrier Persona's booking endpoint: where Norvia's P4 booking Logic App sends a booking request. It answers a
/// success once the request is in the Carrier's inbox, and the same for a request the inbox has already.
/// </summary>
public static partial class CarrierBookingEndpoint
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string ApiKeyHeader = EndpointCredential.ApiKeyHeader;
    private const string Flow = "carrier-booking";

    public static IEndpointRouteBuilder MapCarrierBookingEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal; what it asks of the caller is the Carrier's credential, checked below.
        endpoints.MapPost("/carrier/v1/{carrierId}/bookings", ReceiveAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<ReceiveCarrierBookingResult>, ProblemHttpResult, UnauthorizedHttpResult>> ReceiveAsync(
        string carrierId,
        [FromHeader(Name = ApiKeyHeader)] string? apiKey,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        CarrierBookingRequestBody body,
        IMediator mediator,
        IOptions<CarrierEndpointOptions> options,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        var logger = loggers.CreateLogger(typeof(CarrierBookingEndpoint));
        using var trail = logger.BeginScope(EndpointTracking.Keys(
            Flow, carrierId, correlationId, ("shipmentNumber", body.ShipmentNumber), ("carrierBookingNumber", body.CarrierBookingNumber)));

        // Who the caller is comes first, so an unknown Carrier and a wrong credential look the same from outside.
        if (!EndpointCredential.IsOf(carrierId, apiKey, options.Value.ApiKeys))
        {
            using var refused = logger.BeginScope(EndpointTracking.Outcome("Refused", "CredentialInvalid"));
            LogRefused(logger, carrierId);
            return TypedResults.Unauthorized();
        }

        if (Problems(correlationId, body) is { Count: > 0 } problems)
        {
            using var invalid = logger.BeginScope(EndpointTracking.Outcome("Failed", "BookingRequestInvalid"));
            LogInvalid(logger, carrierId);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "The booking request is incomplete.", detail: string.Join(' ', problems));
        }

        var result = await mediator.Send(
            new ReceiveCarrierBookingCommand(carrierId, body.CarrierBookingNumber!, body.ShipmentNumber!, correlationId!, DetailsOf(body)), cancellationToken);
        if (!result.IsSuccess)
        {
            using var unknown = logger.BeginScope(EndpointTracking.Outcome("Failed", "CarrierUnknown"));
            LogUnknownCarrier(logger, carrierId);
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Carrier.");
        }

        using var received = logger.BeginScope(EndpointTracking.Outcome("Succeeded", failureCode: null));
        LogReceived(logger, result.Value.Repeated);
        return TypedResults.Ok(result.Value);
    }

    private static List<string> Problems(string? correlationId, CarrierBookingRequestBody body)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            problems.Add($"The request needs an {CorrelationIdHeader}.");
        }

        if (string.IsNullOrWhiteSpace(body.CarrierBookingNumber))
        {
            problems.Add("The request needs a carrierBookingNumber.");
        }

        if (string.IsNullOrWhiteSpace(body.ShipmentNumber))
        {
            problems.Add("The request needs a shipmentNumber.");
        }

        return problems;
    }

    private static CarrierBookingDetails DetailsOf(CarrierBookingRequestBody body) => new(
        new CarrierBookingStop(body.CollectionStreet, body.CollectionPostcode, body.CollectionCity, body.CollectionCountry, body.CollectFrom, body.CollectUntil),
        new CarrierBookingStop(body.DeliveryStreet, body.DeliveryPostcode, body.DeliveryCity, body.DeliveryCountry, body.DeliverFrom, body.DeliverUntil),
        body.ConsigneeName,
        [.. (body.Goods ?? []).Select(line => new CarrierBookingGoodsLine(
            line.PackageType, line.Quantity, line.WeightKg, line.LengthCm, line.WidthCm, line.HeightCm))]);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a booking request for Carrier {CarrierId}: the credential is missing or wrong")]
    private static partial void LogRefused(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an incomplete booking request for Carrier {CarrierId}")]
    private static partial void LogInvalid(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Carrier Persona has the ID {CarrierId}")]
    private static partial void LogUnknownCarrier(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Put the booking request in the Carrier's inbox (repeated: {Repeated})")]
    private static partial void LogReceived(ILogger logger, bool repeated);
}
