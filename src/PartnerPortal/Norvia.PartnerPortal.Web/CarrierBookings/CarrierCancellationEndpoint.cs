using System.Text.RegularExpressions;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierBookings.Cancel;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>
/// A Carrier Persona's cancellation endpoint: where Norvia's P7 Carrier cancellation Logic App tells the Carrier it
/// withdrew or cancelled a booking. It only acknowledges: the Carrier can't refuse. The Carrier booking number is the key,
/// so a cancellation sent again is acknowledged and changes nothing.
/// </summary>
public static partial class CarrierCancellationEndpoint
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string ApiKeyHeader = EndpointCredential.ApiKeyHeader;
    private const string Flow = "carrier-cancellation";
    private const int MaxReasonLength = 500;
    private const string WithdrawnKind = "WITHDRAWN";
    private const string CancelledKind = "CANCELLED";

    public const string Route = "/carrier/v1/{carrierId}/cancellations";

    /// <summary>Where a Carrier Persona's cancellations are sent.</summary>
    public static string PathOf(string carrierId) => Route.Replace("{carrierId}", carrierId, StringComparison.Ordinal);

    public static IEndpointRouteBuilder MapCarrierCancellationEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal; what it asks of the caller is the Carrier's credential, checked below.
        endpoints.MapPost(Route, ReceiveAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<ReceiveCarrierCancellationResult>, ProblemHttpResult, UnauthorizedHttpResult>> ReceiveAsync(
        string carrierId,
        [FromHeader(Name = ApiKeyHeader)] string? apiKey,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        CarrierCancellationBody body,
        IMediator mediator,
        IOptions<CarrierEndpointOptions> options,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        var logger = loggers.CreateLogger(typeof(CarrierCancellationEndpoint));
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
            using var invalid = logger.BeginScope(EndpointTracking.Outcome("Failed", "CarrierCancellationInvalid"));
            LogInvalid(logger, carrierId);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "The cancellation is invalid.", detail: string.Join(' ', problems));
        }

        var kind = body.Kind == CancelledKind ? CarrierCancellationKind.Cancelled : CarrierCancellationKind.Withdrawn;
        var result = await mediator.Send(
            new ReceiveCarrierCancellationCommand(carrierId, body.CarrierBookingNumber!, kind, body.ReasonText, correlationId!), cancellationToken);
        if (result.Status == Ardalis.Result.ResultStatus.Invalid)
        {
            using var invalid = logger.BeginScope(EndpointTracking.Outcome("Failed", "CarrierCancellationInvalid"));
            LogInvalid(logger, carrierId);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "The cancellation is invalid.", detail: string.Join(' ', result.ValidationErrors.Select(error => error.ErrorMessage)));
        }

        if (!result.IsSuccess)
        {
            using var unknown = logger.BeginScope(EndpointTracking.Outcome("Failed", "CarrierUnknown"));
            LogUnknownCarrier(logger, carrierId);
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Carrier.");
        }

        using var received = logger.BeginScope(EndpointTracking.Outcome("Succeeded", failureCode: null));
        LogReceived(logger, result.Value.Applied, result.Value.Repeated);
        return TypedResults.Ok(result.Value);
    }

    // The schema's rules (carrier-cancellation.v1.schema.json), so what the endpoint takes is what the format allows.
    private static List<string> Problems(string? correlationId, CarrierCancellationBody body)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            problems.Add($"The request needs an {CorrelationIdHeader}.");
        }

        if (body.CarrierBookingNumber is null || !CarrierBookingNumber().IsMatch(body.CarrierBookingNumber))
        {
            problems.Add("The cancellation needs a carrierBookingNumber such as CB-0000001.");
        }

        if (body.ShipmentNumber is null || !ShipmentNumber().IsMatch(body.ShipmentNumber))
        {
            problems.Add("The cancellation needs a shipmentNumber such as SH-0000042.");
        }

        switch (body.Kind)
        {
            case CancelledKind:
                Require(problems, body.CarrierBookingReference, "carrierBookingReference");
                Require(problems, body.ReasonText, "reasonText");
                if (body.ReasonText?.Length > MaxReasonLength)
                {
                    problems.Add($"The reasonText is at most {MaxReasonLength} characters.");
                }

                break;
            case WithdrawnKind:
                if (body.CarrierBookingReference is not null || body.ReasonText is not null)
                {
                    problems.Add("A withdrawal carries no carrierBookingReference and no reasonText.");
                }

                break;
            default:
                problems.Add($"The cancellation needs a kind, {WithdrawnKind} or {CancelledKind}.");
                break;
        }

        return problems;
    }

    private static void Require(List<string> problems, string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"The cancellation needs a {name}.");
        }
    }

    [GeneratedRegex("^CB-[0-9]{7}$")]
    private static partial Regex CarrierBookingNumber();

    [GeneratedRegex("^SH-[0-9]{7}$")]
    private static partial Regex ShipmentNumber();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a cancellation for Carrier {CarrierId}: the credential is missing or wrong")]
    private static partial void LogRefused(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an invalid cancellation for Carrier {CarrierId}")]
    private static partial void LogInvalid(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Carrier Persona has the ID {CarrierId}")]
    private static partial void LogUnknownCarrier(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Acknowledged the cancellation (applied: {Applied}, repeated: {Repeated})")]
    private static partial void LogReceived(ILogger logger, bool applied, bool repeated);
}
