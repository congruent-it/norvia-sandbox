using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>
/// Where a Shipper Persona's browser downloads a file of a Proof of delivery. The portal fetches it through the Norvia
/// Partner API as the Persona, which the browser can't do: the Persona's token and subscription key stay in the portal.
/// </summary>
public static class ProofOfDeliveryFileEndpoint
{
    public const string Route = "/personas/{personaId}/tracking/{transportOrderNumber}/proof-of-delivery/files/{fileId:guid}";

    public static IEndpointRouteBuilder MapProofOfDeliveryFileEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal, which has no login: who it acts as is the Persona's own token.
        endpoints.MapGet(Route, DownloadAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadAsync(
        string personaId, string transportOrderNumber, Guid fileId, IPartnerPortal portal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portal);

        var answer = await portal.DownloadProofOfDeliveryFileAsync(personaId, transportOrderNumber, fileId, cancellationToken);
        return answer.Outcome switch
        {
            // With a body, so the status code pages don't turn it into the Not Found page.
            TrackingOutcome.Found => TypedResults.File(answer.Value!.Content, answer.Value.ContentType, answer.Value.Name),
            TrackingOutcome.NotFound => TypedResults.Problem(answer.Text, statusCode: StatusCodes.Status404NotFound, title: "File not found"),
            TrackingOutcome.Refused => TypedResults.Problem(answer.Text, statusCode: StatusCodes.Status502BadGateway, title: "File refused"),
            _ => TypedResults.Problem(answer.Text, statusCode: StatusCodes.Status503ServiceUnavailable, title: "File unavailable"),
        };
    }
}
