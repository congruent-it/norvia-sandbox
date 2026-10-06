using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The Norvia Partner API's "submit Carrier Proof of delivery", played locally: API Management's checks at the edge, then
/// the body stored unparsed in the inbound container, only then the blob's reference on the Proof of delivery queue, and
/// 202 Accepted (ADR 0009, ADR 0023). Like the policy it calls no Logic App and never the TMS: the Carrier Proof of
/// delivery Logic App's work is done later, from the queue.
/// </summary>
internal static partial class SubmitCarrierProofOfDelivery
{
    /// <summary>The largest body accepted, as the policy's limit.</summary>
    internal const long MaxBodyBytes = 25 * 1024 * 1024;

    public static IEndpointRouteBuilder MapSubmitCarrierProofOfDelivery(this IEndpointRouteBuilder endpoints)
    {
        // Authorization is the edge's own checks, in API Management's order, not the ASP.NET Core pipeline's.
        endpoints.MapPost("/partner/v1/carrier-proofs-of-delivery", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<ContentHttpResult> HandleAsync(
        HttpContext context,
        PartnerEdge edge,
        IInboundProofsOfDelivery inbound,
        IProofOfDeliveryQueue queue,
        IOptions<PartnerApiOptions> options,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);

        // A new one for each message, whatever the caller sent, as API Management creates it.
        var correlationId = Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        var logger = loggers.CreateLogger(typeof(SubmitCarrierProofOfDelivery));
        using var trail = logger.BeginScope(new Dictionary<string, object?> { [TrackingKeys.CorrelationId] = correlationId, [TrackingKeys.Flow] = TrackingKeys.CarrierProofOfDelivery });

        var check = await edge.CheckAsync(context.Request, EdgeRule.SubmitCarrierProofOfDelivery, context.RequestAborted);
        if (check.PartyId is not { } carrierId)
        {
            using var refused = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, check.FailureCode));
            LogRefused(logger, check.RefusalStatus, check.FailureCode);
            return check.RefusalStatus == StatusCodes.Status401Unauthorized
                ? Answer(check.RefusalStatus, new JsonObject { ["statusCode"] = check.RefusalStatus, ["message"] = check.RefusalText })
                : Answer(check.RefusalStatus, new JsonObject { ["messageId"] = correlationId, ["text"] = check.RefusalText });
        }

        var body = await ReadAtMostAsync(context.Request, MaxBodyBytes, context.RequestAborted);
        if (body is null)
        {
            using var tooLarge = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Refused, "ProofOfDeliveryTooLarge"));
            LogTooLarge(logger);
            return Answer(StatusCodes.Status413PayloadTooLarge, new JsonObject { ["messageId"] = correlationId, ["text"] = "A Proof of delivery is at most 25 MB." });
        }

        // The tracking contract's keys (coding guidelines §7) go on the message as application properties, so a message
        // Service Bus dead-letters itself can still be found. The source message ID is the Carrier's document ID, when it has one.
        var properties = new Dictionary<string, string>
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.PartyId] = carrierId,
            [TrackingKeys.Flow] = TrackingKeys.CarrierProofOfDelivery,
        };
        if (JsonText.Field(body, "documentId") is { } documentId)
        {
            properties[TrackingKeys.SourceMessageId] = documentId;
        }

        var keys = properties.Where(property => property.Key is not (TrackingKeys.CorrelationId or TrackingKeys.Flow))
            .ToDictionary(property => property.Key, property => (object?)property.Value);
        using var party = logger.BeginScope(keys);

        // The claim check: the body unparsed, as a blob named for the correlation ID, and only then its reference.
        try
        {
            await inbound.StoreAsync(correlationId, body, context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            using var failed = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ProofOfDeliveryNotStored"));
            LogNotStored(logger, exception);
            return NotAccepted(correlationId);
        }

        var reference = new JsonObject { ["container"] = options.Value.InboundContainer, ["blobName"] = correlationId }.ToJsonString();
        try
        {
            await queue.EnqueueAsync(new ProofOfDeliveryMessage(reference, correlationId, properties), context.RequestAborted);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            using var failed = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Failed, "ProofOfDeliveryNotQueued"));
            LogNotQueued(logger, exception);
            return NotAccepted(correlationId);
        }

        using var accepted = logger.BeginScope(TrackingKeys.OutcomeOf(TrackingKeys.Succeeded));
        LogAccepted(logger);
        return Answer(StatusCodes.Status202Accepted, new JsonObject { ["messageId"] = correlationId });
    }

    // The body, or null when it is over the limit: by its declared length, or by reading one byte past the limit.
    private static async Task<byte[]?> ReadAtMostAsync(HttpRequest request, long limit, CancellationToken cancellationToken)
    {
        if (request.ContentLength > limit)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ContentHttpResult NotAccepted(string correlationId) =>
        Answer(StatusCodes.Status503ServiceUnavailable, new JsonObject
        {
            ["messageId"] = correlationId,
            ["text"] = "The Proof of delivery was not accepted. Send it again.",
        });

    private static ContentHttpResult Answer(int statusCode, JsonObject body) =>
        TypedResults.Content(body.ToJsonString(), "application/json", statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused at the edge with {StatusCode} ({FailureCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string? failureCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused: the Proof of delivery is over 25 MB")]
    private static partial void LogTooLarge(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Accepted: stored, and its reference put on the Proof of delivery queue")]
    private static partial void LogAccepted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Proof of delivery could not be stored in the inbound container")]
    private static partial void LogNotStored(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Proof of delivery's reference could not be put on the Proof of delivery queue")]
    private static partial void LogNotQueued(ILogger logger, Exception exception);
}
