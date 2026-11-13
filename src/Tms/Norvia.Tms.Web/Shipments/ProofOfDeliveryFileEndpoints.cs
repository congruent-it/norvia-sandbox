using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Tms.Application.ProofsOfDelivery.Read;

namespace Norvia.Tms.Web.Shipments;

/// <summary>
/// Serves a Proof of delivery's files to the Planner, read through the TMS query side like the rest of the UI: shown in the
/// browser by default, saved when asked to download. Anonymous like the rest of the TMS UI (issue #22).
/// </summary>
public static class ProofOfDeliveryFileEndpoints
{
    public const string Route = "/shipments/{shipmentNumber}/proof-of-delivery/files/{fileId:guid}";

    /// <summary>Where a Proof of delivery's file is shown, or saved with <paramref name="download"/>.</summary>
    public static string PathOf(string shipmentNumber, Guid fileId, bool download = false) =>
        $"/shipments/{Uri.EscapeDataString(shipmentNumber)}/proof-of-delivery/files/{fileId}{(download ? "?download=true" : string.Empty)}";

    public static IEndpointRouteBuilder MapProofOfDeliveryFiles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, GetAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> GetAsync(
        string shipmentNumber, Guid fileId, bool? download, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        var result = await mediator.Send(new GetProofOfDeliveryFileQuery(shipmentNumber, fileId), cancellationToken);
        if (result.IsExpired())
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: $"This Proof of delivery file of Shipment {shipmentNumber} has expired: the retention period deleted it. The Proof of delivery itself is kept.",
                extensions: new Dictionary<string, object?> { ["code"] = ProofOfDeliveryFileExpiry.Marker });
        }

        if (result.Status != ResultStatus.Ok)
        {
            // With a body, so the status code pages don't turn it into the Not Found page.
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} has no such Proof of delivery file.");
        }

        return TypedResults.File(result.Value.Content, result.Value.ContentType, download == true ? result.Value.Name : null);
    }
}
