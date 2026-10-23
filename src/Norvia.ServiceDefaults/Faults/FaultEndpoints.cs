using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Norvia.ServiceDefaults.Faults;

public static class FaultEndpoints
{
    /// <summary>The body of a switch. <see cref="DurationSeconds"/> is left out to use the default duration; it is ignored when the fault goes off.</summary>
    public sealed record SwitchFaultRequest(bool On, int? DurationSeconds = null);

    /// <summary>
    /// Maps the application's fault switches on <paramref name="faults"/>: <c>GET /</c> lists them and <c>PUT /{name}</c> switches one on or off.
    /// This is the only way to expose a fault, and every endpoint it maps requires the <see cref="FaultRoles.DemoFault"/> app role,
    /// so no fault switch can be left open. The caller maps it on the group where the application wants the switches, such as a versioned <c>/v1/faults</c>.
    /// </summary>
    public static void MapFaults(this IEndpointRouteBuilder faults)
    {
        ArgumentNullException.ThrowIfNull(faults);

        faults.MapGet("/", (IFaults state) => Results.Ok(state.List()))
            .WithName("ListFaults")
            .WithSummary("List the faults this application owns, with whether each is on and how long it has left.")
            .RequireAuthorization(policy => policy.RequireRole(FaultRoles.DemoFault));

        faults.MapPut("/{name}", Switch)
            .WithName("SwitchFault")
            .WithSummary("Switch one of this application's faults on, for a duration after which it expires by itself, or off.")
            .WithDescription("Only a caller holding the app role Demo.Fault may call it. A duration longer than the application allows (a few minutes by default) is refused with 400.")
            .RequireAuthorization(policy => policy.RequireRole(FaultRoles.DemoFault));
    }

    private static IResult Switch(string name, SwitchFaultRequest request, IFaults state, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("Norvia.ServiceDefaults.Faults");
        if (!request.On)
        {
            if (!state.SwitchOff(name, out var off))
            {
                return NotFound(name);
            }

            FaultLog.SwitchedOff(logger, name);
            return Results.Ok(off);
        }

        var duration = request.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        switch (state.SwitchOn(name, duration, out var on))
        {
            case SwitchOnResult.Switched:
                FaultLog.SwitchedOn(logger, name, on!.Until);
                return Results.Ok(on);
            case SwitchOnResult.UnknownFault:
                return NotFound(name);
            default:
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["durationSeconds"] = ["The duration must be more than zero and no longer than the application allows."] });
        }
    }

    private static IResult NotFound(string name) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The application has no such fault.", extensions: new Dictionary<string, object?> { ["fault"] = name });
}
