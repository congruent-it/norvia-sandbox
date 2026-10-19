using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application;
using Norvia.PartnerPortal.Application.CarrierInvoices.Outcome;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierInvoices;

/// <summary>
/// A Carrier Persona's invoice outcome endpoint: where Norvia's E2 Carrier invoice outcome Logic App tells the Carrier what
/// became of a Carrier invoice. It only acknowledges: it answers a success once the outcome is stored, and the same for one
/// the Persona has already, by the Carrier's invoice number. It has no answer modes, and the Carrier can't reply.
/// </summary>
public static partial class CarrierInvoiceOutcomeEndpoint
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string ApiKeyHeader = EndpointCredential.ApiKeyHeader;
    private const string Flow = "carrier-invoice-outcome";

    public const string Route = "/carrier/v1/{carrierId}/invoice-outcomes";

    /// <summary>Where a Carrier Persona is told the outcome of its Carrier invoices.</summary>
    public static string PathOf(string carrierId) => Route.Replace("{carrierId}", carrierId, StringComparison.Ordinal);

    public static IEndpointRouteBuilder MapCarrierInvoiceOutcomeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal; what it asks of the caller is the Carrier's credential, checked below.
        endpoints.MapPost(Route, ReceiveAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<ReceiveCarrierInvoiceOutcomeResult>, ProblemHttpResult, UnauthorizedHttpResult>> ReceiveAsync(
        string carrierId,
        [FromHeader(Name = ApiKeyHeader)] string? apiKey,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        CarrierInvoiceOutcomeBody body,
        IConflictRetrier retrier,
        IOptions<CarrierEndpointOptions> options,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(retrier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        var logger = loggers.CreateLogger(typeof(CarrierInvoiceOutcomeEndpoint));
        using var trail = logger.BeginScope(EndpointTracking.Keys(Flow, carrierId, correlationId, ("documentNumber", body.InvoiceNo)));

        // Who the caller is comes first, so an unknown Carrier and a wrong credential look the same from outside.
        if (!EndpointCredential.IsOf(carrierId, apiKey, options.Value.ApiKeys))
        {
            using var refused = logger.BeginScope(EndpointTracking.Outcome("Refused", "CredentialInvalid"));
            LogRefused(logger, carrierId);
            return TypedResults.Unauthorized();
        }

        if (Problems(correlationId, body) is { Count: > 0 } problems)
        {
            using var invalid = logger.BeginScope(EndpointTracking.Outcome("Failed", "CarrierInvoiceOutcomeInvalid"));
            LogInvalid(logger, carrierId);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "The outcome is invalid.", detail: string.Join(' ', problems));
        }

        var command = new ReceiveCarrierInvoiceOutcomeCommand(carrierId, correlationId!, body.InvoiceNo!, body.ApprovedAmount!.Value, LinesOf(body));
        // Two copies of one outcome can arrive at once; the one that loses the race is told it is a repeat.
        var result = await retrier.RunAsync(mediator => mediator.Send(command, cancellationToken).AsTask());
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

    // The schema's rules (carrier-invoice-outcome.v1.schema.json), so what the endpoint takes is what the format allows.
    private static List<string> Problems(string? correlationId, CarrierInvoiceOutcomeBody body)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            problems.Add($"The request needs an {CorrelationIdHeader}.");
        }

        if (string.IsNullOrWhiteSpace(body.InvoiceNo))
        {
            problems.Add("The outcome needs an invoiceNo.");
        }

        Amount(problems, body.ApprovedAmount, "approvedAmount");

        if (body.Lines is not { Count: > 0 })
        {
            problems.Add("The outcome needs at least one line.");
            return problems;
        }

        foreach (var line in body.Lines)
        {
            if (line is null)
            {
                problems.Add("A line can't be empty.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.BookingRef))
            {
                problems.Add("Every line needs a bookingRef.");
            }

            if (!CarrierInvoiceOutcomeResults.TryParse(line.Result, out _))
            {
                problems.Add($"Every line needs a result, one of {string.Join(", ", CarrierInvoiceOutcomeResults.WireNames)}.");
            }

            Amount(problems, line.AcceptedAmount, "acceptedAmount of every line");
        }

        return problems.Distinct().ToList();
    }

    // An amount is not negative and has at most two decimals.
    private static void Amount(List<string> problems, decimal? value, string name)
    {
        if (value is not { } amount || amount < 0 || decimal.Round(amount, 2) != amount)
        {
            problems.Add($"The {name} must be an amount in euros, not negative, with at most two decimals.");
        }
    }

    // Only called for a body that passed Problems, so what it holds parses.
    private static List<CarrierInvoiceOutcomeLine> LinesOf(CarrierInvoiceOutcomeBody body) =>
    [
        .. body.Lines!.Select(line =>
        {
            _ = CarrierInvoiceOutcomeResults.TryParse(line!.Result, out var result);
            return new CarrierInvoiceOutcomeLine(line.BookingRef!.Trim(), result, line.AcceptedAmount!.Value, string.IsNullOrWhiteSpace(line.Remark) ? null : line.Remark.Trim());
        }),
    ];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an invoice outcome for Carrier {CarrierId}: the credential is missing or wrong")]
    private static partial void LogRefused(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an invalid invoice outcome for Carrier {CarrierId}")]
    private static partial void LogInvalid(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Carrier Persona has the ID {CarrierId}")]
    private static partial void LogUnknownCarrier(ILogger logger, string carrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored the outcome of a Carrier invoice (repeated: {Repeated})")]
    private static partial void LogReceived(ILogger logger, bool repeated);
}
