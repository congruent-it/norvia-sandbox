using System.Security.Cryptography;
using System.Text;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Reset;

namespace Norvia.PartnerPortal.Web.Reset;

/// <summary>Where the Simulator's Reset estate empties the Partner portal (ADR 0039). The answers have the shape of the TMS API's and the Driver app API's.</summary>
public static class ResetEndpoints
{
    public const string ApiKeyHeader = EndpointCredential.ApiKeyHeader;

    public static IEndpointRouteBuilder MapResetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal; what it asks of the caller is the reset's credential, checked below.
        endpoints.MapGet("/reset/v1", GetReadinessAsync).AllowAnonymous();
        endpoints.MapPost("/reset/v1", ResetAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<ResetReadiness>, UnauthorizedHttpResult>> GetReadinessAsync(
        [FromHeader(Name = ApiKeyHeader)] string? apiKey, IOptions<ResetEndpointOptions> options, IMediator mediator, CancellationToken cancellationToken) =>
        IsCaller(apiKey, options.Value)
            ? TypedResults.Ok(await mediator.Send(new GetResetReadinessQuery(), cancellationToken))
            : TypedResults.Unauthorized();

    public static async Task<Results<Ok<ResetReport>, ProblemHttpResult, UnauthorizedHttpResult>> ResetAsync(
        [FromHeader(Name = ApiKeyHeader)] string? apiKey, IOptions<ResetEndpointOptions> options, IMediator mediator, CancellationToken cancellationToken)
    {
        if (!IsCaller(apiKey, options.Value))
        {
            return TypedResults.Unauthorized();
        }

        var result = await mediator.Send(new ResetEstateCommand(), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "The reset is switched off.", detail: result.Errors.First(),
                extensions: new Dictionary<string, object?> { ["code"] = ResetEstateHandler.DisabledCode });
    }

    // With no credential configured nobody is the caller: a portal that isn't told the key must refuse every reset.
    private static bool IsCaller(string? apiKey, ResetEndpointOptions options) =>
        !string.IsNullOrEmpty(apiKey)
        && !string.IsNullOrEmpty(options.ApiKey)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(options.ApiKey));
}
