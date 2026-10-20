using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's "submit Carrier invoice", played locally: API Management's checks at the edge, then the invoice handed to the
/// Carrier invoice Logic App's stand-in (<c>Norvia.Integration.LocalCarrierInvoiceWorker</c>), whose answer goes back to the Carrier at once
/// (ADR 0023, ADR 0043).
/// </summary>
internal static partial class SubmitCarrierInvoice
{
    public static IEndpointRouteBuilder MapSubmitCarrierInvoice(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/partner/v1/carrier-invoices", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(HttpContext context, PartnerEdge edge, CarrierInvoiceLogicApp logicApp, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(logicApp);
        ArgumentNullException.ThrowIfNull(loggers);

        // A new one for each invoice, whatever the caller sent, as API Management creates it.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = loggers.CreateLogger(typeof(SubmitCarrierInvoice));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.CarrierInvoice });

        var check = await edge.CheckAsync(context.Request, EdgeRule.SubmitCarrierInvoice, context.RequestAborted);
        if (check.PartyId is not { } carrierId)
        {
            // As the policy's trace metadata records a refusal.
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Answer(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText }.ToJsonString())
                : Answer(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText }.ToJsonString());
        }

        using var party = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.PartyId] = carrierId });
        using var reader = new StreamReader(context.Request.Body);
        var invoice = await reader.ReadToEndAsync(context.RequestAborted);

        var (statusCode, body) = await logicApp.SubmitAsync(invoice, carrierId, correlationId, context.RequestAborted);
        if (statusCode == 0)
        {
            statusCode = StatusCodes.Status502BadGateway;
            body = new JsonObject
            {
                ["result"] = "NOT_RECEIVED",
                ["messageId"] = correlationId,
                ["text"] = "Norvia could not take your invoice in, so nothing was recorded. Please send it again later.",
            }.ToJsonString();
        }

        // The outcome of the invoice the Carrier got, as the policy's outbound section traces it.
        using var outcome = logger.BeginScope(OutcomeOf(statusCode));
        LogAnswered(logger, statusCode);
        return Answer(statusCode, body);
    }

    private static Dictionary<string, object?> OutcomeOf(int statusCode) => statusCode switch
    {
        200 or 201 => TrackingKeys.OutcomeOf(TrackingKeys.Succeeded),
        400 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "CarrierInvoiceUnreadable"),
        403 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "CarrierNotAllowed"),
        409 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "InvoiceNumberUsed"),
        422 => TrackingKeys.OutcomeOf(TrackingKeys.Refused, "CarrierNotFound"),
        _ => TrackingKeys.OutcomeOf(TrackingKeys.Failed, "InvoiceNotReceived"),
    };

    private static ContentHttpResult Answer(int statusCode, string body) =>
        TypedResults.Content(body, "application/json", statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Answered the Carrier with {StatusCode}")]
    private static partial void LogAnswered(ILogger logger, int statusCode);
}
