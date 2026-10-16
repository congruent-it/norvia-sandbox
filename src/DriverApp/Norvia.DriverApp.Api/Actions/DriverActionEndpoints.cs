using System.Security.Claims;
using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Norvia.DriverApp.Api.Reports;
using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Application.Reports.EndTrip;
using Norvia.DriverApp.Application.Reports.ReportStop;
using Norvia.DriverApp.Application.Reports.StartTrip;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.GetForDriver;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Norvia.DriverApp.Api.Actions;

/// <summary>
/// The Driver's actions as an API, for a caller that acts as the Driver without the Driver app's screens, such as the Simulator. It does what the screens'
/// form posts do, through the same commands. The Driver ID comes from the token's one app role, never from the request (ADR 0005, ADR 0037).
/// </summary>
public static class DriverActionEndpoints
{
    public const string DriverPolicy = "Driver";

    private const string RolesClaim = "roles";

    public static IServiceCollection AddDriverActionPolicy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthorizationBuilder().AddPolicy(DriverPolicy, policy => policy.RequireAuthenticatedUser()
            // Exactly one, and not the Logic App's: a token with two Driver IDs would otherwise be an arbitrary one of them.
            .RequireAssertion(context => DriverIdOf(context.User) is not null));
        return services;
    }

    /// <summary>The Driver ID of a Driver's token: its one app role, unless that is the role of the Driver reports Logic App.</summary>
    public static string? DriverIdOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var roles = user.FindAll(RolesClaim).Select(claim => claim.Value).ToList();
        return roles is [{ Length: > 0 } role] && role != ReportOutcomeEndpoints.ReportOutcomeRole ? role : null;
    }

    public static IEndpointRouteBuilder MapDriverActions(this IEndpointRouteBuilder app)
    {
        var driver = app.NewVersionedApi("DriverActions")
            .MapGroup("/v{version:apiVersion}/driver/trips")
            .HasApiVersion(1)
            .RequireAuthorization(DriverPolicy);

        driver.MapGet("/", ListAsync)
            .WithName("ListMyTrips")
            .WithSummary("List the Driver's own Trips, the latest version of each, with their Stops in order and the Driver's reports on them.");

        driver.MapPost("/{tripNumber}/start", StartAsync)
            .WithName("DriverStartTrip")
            .WithSummary("Start a Dispatched Trip as its Driver.")
            .WithDescription("A Trip that isn't the token's Driver's is TripNotFound (404). The report travels to the TMS as an event, and the TMS decides whether the Trip can start.");

        driver.MapPost("/{tripNumber}/end", EndAsync)
            .WithName("DriverEndTrip")
            .WithSummary("End an In progress Trip as its Driver, once every Stop has an outcome.")
            .WithDescription("TripNotFound (404), or ReportNotAvailable (409) while the Trip isn't started or a Stop has no outcome.");

        driver.MapPost("/{tripNumber}/stops/{shipmentNumber}/{kind}/{action}", ReportStopAsync)
            .WithName("DriverReportStop")
            .WithSummary("Report Collected, Collection failed or Delivery failed at a Stop, or damage at any.")
            .WithDescription(
                "kind is collection or delivery and action is collected, collection-failed, delivery-failed or damaged, as on the Driver app's screens. A failure and damage need a remark. "
                + "StopNotFound (404), ReportInvalid (400) or ReportNotAvailable (409) when the Stop can't be reported on now.");

        driver.MapPost("/{tripNumber}/stops/{shipmentNumber}/delivery/deliver", DeliverAsync)
            .WithName("DriverDeliver")
            .WithSummary("Deliver a delivery Stop with a Proof of delivery.")
            .WithDescription("receivedBy, condition and a PNG signature are required, with a remark when there are reservations, and 0 to 4 photos. StopNotFound (404), ReportInvalid (400) or ReportNotAvailable (409).");

        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, IMediator mediator, CancellationToken cancellationToken) =>
        Results.Ok(await mediator.Send(new GetDriverTripsQuery(DriverIdOf(user)!), cancellationToken));

    private static async Task<IResult> StartAsync(string tripNumber, ClaimsPrincipal user, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new StartTripCommand(tripNumber, DriverIdOf(user)!), cancellationToken);
        return Answer(result, "TripNotFound", value => new DriverReportAccepted(value.ReportId, value.CorrelationId, value.AlreadyReported));
    }

    private static async Task<IResult> EndAsync(string tripNumber, ClaimsPrincipal user, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new EndTripCommand(tripNumber, DriverIdOf(user)!), cancellationToken);
        return Answer(result, "TripNotFound", value => new DriverReportAccepted(value.ReportId, value.CorrelationId, value.AlreadyReported));
    }

    private static async Task<IResult> ReportStopAsync(
        string tripNumber, string shipmentNumber, string kind, string action, StopReportRequest? request, ClaimsPrincipal user, IMediator mediator, CancellationToken cancellationToken)
    {
        if (!TryParse(kind, out StopKindDto stop) || !TryParse(action, out StopActionDto what))
        {
            return Problem(StatusCodes.Status404NotFound, "StopNotFound", "The Driver app has no such Stop or report.");
        }

        var result = await mediator.Send(new ReportStopCommand(tripNumber, DriverIdOf(user)!, shipmentNumber, stop, what, request?.Remark), cancellationToken);
        return Answer(result, "StopNotFound", value => new DriverReportAccepted(value.ReportId, value.CorrelationId, value.AlreadyReported));
    }

    private static async Task<IResult> DeliverAsync(
        string tripNumber, string shipmentNumber, DeliverRequest? request, ClaimsPrincipal user, IMediator mediator, CancellationToken cancellationToken)
    {
        request ??= new DeliverRequest(null, null, null, null, null);
        var command = new DeliverCommand(
            tripNumber, DriverIdOf(user)!, shipmentNumber, request.ReceivedBy, request.Condition, request.Remark, request.Signature?.ToFile(),
            [.. (request.Photos ?? []).Select(photo => photo.ToFile())]);
        var result = await mediator.Send(command, cancellationToken);
        return Answer(result, "StopNotFound", value => new DriverReportAccepted(value.ReportId, value.CorrelationId, value.AlreadyReported));
    }

    private static IResult Answer<T>(Result<T> result, string notFoundCode, Func<T, DriverReportAccepted> accepted) => result.Status switch
    {
        ResultStatus.Ok => Results.Ok(accepted(result.Value)),
        ResultStatus.NotFound => Problem(StatusCodes.Status404NotFound, notFoundCode, "The Driver app has no such Trip or Stop for this Driver."),
        ResultStatus.Invalid => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The report breaks a rule.",
            detail: string.Join(" ", result.ValidationErrors.Select(error => error.ErrorMessage)),
            extensions: new Dictionary<string, object?> { ["code"] = "ReportInvalid", ["fields"] = result.ValidationErrors.Select(error => error.Identifier).Distinct().ToArray() }),
        _ => Problem(StatusCodes.Status409Conflict, "ReportNotAvailable", string.Join(" ", result.Errors)),
    };

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryParse(string segment, out StopKindDto kind)
    {
        kind = segment == "collection" ? StopKindDto.Collection : StopKindDto.Delivery;
        return segment is "collection" or "delivery";
    }

    private static bool TryParse(string segment, out StopActionDto action)
    {
        action = segment switch
        {
            "collected" => StopActionDto.Collected,
            "collection-failed" => StopActionDto.CollectionFailed,
            "delivery-failed" => StopActionDto.DeliveryFailed,
            _ => StopActionDto.Damaged,
        };
        return segment is "collected" or "collection-failed" or "delivery-failed" or "damaged";
    }
}
