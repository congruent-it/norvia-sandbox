using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceWorker;

/// <summary>
/// The workflow's HTTP trigger <c>When_a_Carrier_invoices</c>, played locally. Like the workflow's first step it refuses any caller other than
/// API Management (ADR 0020), whose place the local Partner API takes; the Carrier and the correlation ID come from the caller's headers
/// alone, never from the invoice (ADR 0005).
/// </summary>
internal static partial class CarrierInvoiceTrigger
{
    public static IEndpointRouteBuilder MapCarrierInvoiceTrigger(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/carrier-invoice", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(
        HttpContext context, CarrierInvoiceFlow flow, IOptions<CarrierInvoiceWorkerOptions> options, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
        var carrierId = context.Request.Headers["X-Carrier-ID"].ToString();
        var logger = loggers.CreateLogger(typeof(CarrierInvoiceTrigger));
        if (!await IsApiManagementAsync(context.Request, options.Value.Trigger))
        {
            using var refused = logger.BeginScope(new Dictionary<string, object?>
            {
                [TrackingKeys.CorrelationId] = correlationId,
                [TrackingKeys.Flow] = TrackingKeys.CarrierInvoice,
                [TrackingKeys.Outcome] = TrackingKeys.Refused,
                [TrackingKeys.FailureCode] = "CallerNotApiManagement",
            });
            LogRefused(logger);
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            return TypedResults.Content("Only API Management may call this workflow.", "text/plain", statusCode: StatusCodes.Status403Forbidden);
        }

        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using var reader = new StreamReader(context.Request.Body);
        var invoice = await reader.ReadToEndAsync(context.RequestAborted);

        var answer = await flow.RunAsync(invoice, carrierId, correlationId, context.RequestAborted);
        return TypedResults.Content(answer.Body.ToJsonString(), "application/json", statusCode: answer.StatusCode);
    }

    private static async Task<bool> IsApiManagementAsync(HttpRequest request, LocalTokenOptions trigger)
    {
        var authorization = request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..].Trim() : string.Empty;
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = trigger.Issuer,
            ValidAudience = trigger.Audience,
            IssuerSigningKey = trigger.Key,
        });
        return validation.IsValid
            && validation.SecurityToken is JsonWebToken jwt
            && jwt.TryGetClaim("azp", out var client)
            && client.Value == trigger.ClientId;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused: the caller is not API Management")]
    private static partial void LogRefused(ILogger logger);
}
