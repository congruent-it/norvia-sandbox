using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application;
using Norvia.PartnerPortal.Application.ShipperInvoices.Receive;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.ShipperInvoices;

/// <summary>
/// A Shipper Persona's invoice endpoint: where Norvia's E1 Shipper invoice Logic App sends an invoice or Credit note. It
/// only acknowledges: it answers a success once the document is in the Shipper's invoices inbox, and the same for one the
/// inbox has already, by its document number. It has no answer modes, and the Shipper can't reply.
/// </summary>
public static partial class ShipperInvoiceEndpoint
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string ApiKeyHeader = EndpointCredential.ApiKeyHeader;
    private const string Flow = "shipper-invoice";

    public const string Route = "/shipper/v1/{shipperId}/invoices";

    /// <summary>Where a Shipper Persona's invoices and Credit notes are sent.</summary>
    public static string PathOf(string shipperId) => Route.Replace("{shipperId}", shipperId, StringComparison.Ordinal);

    public static IEndpointRouteBuilder MapShipperInvoiceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal; what it asks of the caller is the Shipper's credential, checked below.
        endpoints.MapPost(Route, ReceiveAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<ReceiveShipperInvoiceResult>, ProblemHttpResult, UnauthorizedHttpResult>> ReceiveAsync(
        string shipperId,
        [FromHeader(Name = ApiKeyHeader)] string? apiKey,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        ShipperInvoiceBody body,
        IConflictRetrier retrier,
        IOptions<ShipperEndpointOptions> options,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(retrier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        var logger = loggers.CreateLogger(typeof(ShipperInvoiceEndpoint));
        using var trail = logger.BeginScope(EndpointTracking.Keys(Flow, shipperId, correlationId, ("documentNumber", body.DocumentNumber)));

        // Who the caller is comes first, so an unknown Shipper and a wrong credential look the same from outside.
        if (!EndpointCredential.IsOf(shipperId, apiKey, options.Value.ApiKeys))
        {
            using var refused = logger.BeginScope(EndpointTracking.Outcome("Refused", "CredentialInvalid"));
            LogRefused(logger, shipperId);
            return TypedResults.Unauthorized();
        }

        if (Problems(correlationId, body) is { Count: > 0 } problems)
        {
            using var invalid = logger.BeginScope(EndpointTracking.Outcome("Failed", "ShipperInvoiceInvalid"));
            LogInvalid(logger, shipperId);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "The invoice is invalid.", detail: string.Join(' ', problems));
        }

        var command = new ReceiveShipperInvoiceCommand(shipperId, correlationId!, ContentOf(body));
        // Two copies of one document can arrive at once; the one that loses the race is told it is a repeat.
        var result = await retrier.RunAsync(mediator => mediator.Send(command, cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            using var unknown = logger.BeginScope(EndpointTracking.Outcome("Failed", "ShipperUnknown"));
            LogUnknownShipper(logger, shipperId);
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Shipper.");
        }

        using var received = logger.BeginScope(EndpointTracking.Outcome("Succeeded", failureCode: null));
        LogReceived(logger, result.Value.Repeated);
        return TypedResults.Ok(result.Value);
    }

    // The schema's rules (shipper-invoice.v1.schema.json), so what the endpoint takes is what the format allows.
    private static List<string> Problems(string? correlationId, ShipperInvoiceBody body)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            problems.Add($"The request needs an {CorrelationIdHeader}.");
        }

        var typeKnown = InvoiceDocumentTypes.TryParse(body.DocumentType, out var type);
        if (!typeKnown)
        {
            problems.Add($"The document needs a documentType, one of {string.Join(", ", InvoiceDocumentTypes.WireNames)}.");
        }

        var prefix = typeKnown && type == InvoiceDocumentType.CreditNote ? "CN" : "INV";
        if (body.DocumentNumber is null || !Matches(DocumentNumberPattern(), body.DocumentNumber) || !body.DocumentNumber.StartsWith(prefix, StringComparison.Ordinal))
        {
            problems.Add(typeKnown
                ? $"The document needs a documentNumber such as {prefix}-2030-000001."
                : "The document needs a documentNumber such as INV-2030-000001 or CN-2030-000001.");
        }

        if (!LocalDateText.TryParse(body.DocumentDate, out _))
        {
            problems.Add("The documentDate must be a local date as dd-MM-yyyy.");
        }

        CreditProblems(problems, typeKnown && type == InvoiceDocumentType.CreditNote, body);
        LineProblems(problems, body.Lines);

        if (body.Currency != "EUR")
        {
            problems.Add("The currency must be EUR.");
        }

        Amount(problems, body.TotalWithoutVat, "totalWithoutVat");
        Amount(problems, body.Vat, "vat");
        Amount(problems, body.TotalWithVat, "totalWithVat");
        return problems;
    }

    private static void CreditProblems(List<string> problems, bool creditNote, ShipperInvoiceBody body)
    {
        if (!creditNote)
        {
            if (body.CreditedInvoiceNumber is not null || body.CreditedTransportOrderNumber is not null || body.Reason is not null)
            {
                problems.Add("Only a CREDIT_NOTE names the invoice and line it credits, and a reason.");
            }

            return;
        }

        if (body.CreditedInvoiceNumber is null || !Matches(InvoiceNumberPattern(), body.CreditedInvoiceNumber))
        {
            problems.Add("A CREDIT_NOTE needs a creditedInvoiceNumber such as INV-2030-000001.");
        }

        if (body.CreditedTransportOrderNumber is null || !Matches(TransportOrderNumberPattern(), body.CreditedTransportOrderNumber))
        {
            problems.Add("A CREDIT_NOTE needs a creditedTransportOrderNumber such as TO-0000042.");
        }

        Require(problems, body.Reason, "reason");
    }

    private static void LineProblems(List<string> problems, IReadOnlyList<ShipperInvoiceLineBody?>? lines)
    {
        if (lines is not { Count: > 0 })
        {
            problems.Add("The document needs at least one line.");
            return;
        }

        foreach (var line in lines)
        {
            if (line is null)
            {
                problems.Add("A line can't be empty.");
                continue;
            }

            if (line.TransportOrderNumber is null || !Matches(TransportOrderNumberPattern(), line.TransportOrderNumber))
            {
                problems.Add("Every line needs a transportOrderNumber such as TO-0000042.");
            }

            Require(problems, line.BookingRef, "bookingRef on every line");
            if (!LocalDateText.TryParse(line.DeliveryDate, out _))
            {
                problems.Add("The deliveryDate of every line must be a local date as dd-MM-yyyy.");
            }

            Amount(problems, line.LineTotal, "lineTotal of every line");
            if (line.ChargeLines is not { Count: > 0 })
            {
                problems.Add("Every line needs at least one charge line.");
                continue;
            }

            foreach (var charge in line.ChargeLines)
            {
                if (charge is null || !ChargeCodes.TryParse(charge.Code, out _))
                {
                    problems.Add($"Every charge line needs a code, one of {string.Join(", ", ChargeCodes.WireNames)}.");
                    continue;
                }

                Amount(problems, charge.Amount, "amount of every charge line");
            }
        }
    }

    // Only called for a body that passed Problems, so what it holds parses.
    private static ShipperInvoiceContent ContentOf(ShipperInvoiceBody body)
    {
        _ = InvoiceDocumentTypes.TryParse(body.DocumentType, out var type);
        _ = LocalDateText.TryParse(body.DocumentDate, out var date);
        var credit = type == InvoiceDocumentType.CreditNote
            ? new CreditedLine(body.CreditedInvoiceNumber!, body.CreditedTransportOrderNumber!, body.Reason!)
            : null;
        var lines = body.Lines!.Select(line => LineOf(line!)).ToList();
        return new ShipperInvoiceContent(type, body.DocumentNumber!, date, credit, lines, body.TotalWithoutVat!.Value, body.Vat!.Value, body.TotalWithVat!.Value);
    }

    private static ShipperInvoiceLine LineOf(ShipperInvoiceLineBody line)
    {
        _ = LocalDateText.TryParse(line.DeliveryDate, out var delivered);
        var charges = line.ChargeLines!.Select(charge =>
        {
            _ = ChargeCodes.TryParse(charge!.Code, out var code);
            return new ShipperChargeLine(code, charge.Amount!.Value);
        }).ToList();
        return new ShipperInvoiceLine(line.TransportOrderNumber!, line.BookingRef!, delivered, charges, line.LineTotal!.Value);
    }

    private static void Require(List<string> problems, string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"The document needs a {name}.");
        }
    }

    // An amount is not negative and has at most two decimals.
    private static void Amount(List<string> problems, decimal? value, string name)
    {
        if (value is not { } amount || amount < 0 || decimal.Round(amount, 2) != amount)
        {
            problems.Add($"The {name} must be an amount in euros, not negative, with at most two decimals.");
        }
    }

    private static bool Matches(Regex pattern, string value) => pattern.IsMatch(value);

    [GeneratedRegex("^(INV|CN)-[0-9]{4}-[0-9]{6}$")]
    private static partial Regex DocumentNumberPattern();

    [GeneratedRegex("^INV-[0-9]{4}-[0-9]{6}$")]
    private static partial Regex InvoiceNumberPattern();

    [GeneratedRegex("^TO-[0-9]{7}$")]
    private static partial Regex TransportOrderNumberPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an invoice for Shipper {ShipperId}: the credential is missing or wrong")]
    private static partial void LogRefused(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an invalid invoice for Shipper {ShipperId}")]
    private static partial void LogInvalid(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Shipper Persona has the ID {ShipperId}")]
    private static partial void LogUnknownShipper(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Put the invoice in the Shipper's inbox (repeated: {Repeated})")]
    private static partial void LogReceived(ILogger logger, bool repeated);
}
