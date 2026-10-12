using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Norvia.DriverApp.Application.Reports.ReportOutcome;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Norvia.DriverApp.Api.Reports;

public static class ReportOutcomeEndpoints
{
    /// <summary>The app role only the P12 Driver reports Logic App's managed identity holds (ADR 0016).</summary>
    public const string ReportOutcomeRole = "DriverReports.ReportOutcome";

    public static IEndpointRouteBuilder MapReportOutcomes(this IEndpointRouteBuilder app)
    {
        // The Driver reports Logic App tells the Driver app what became of a report, so the Driver sees Recorded, Refused or Not recorded. The
        // identity comes from the token, never from the payload (ADR 0005). Commands are idempotent: an outcome said twice changes nothing.
        app.NewVersionedApi("ReportOutcomes")
            .MapGroup("/v{version:apiVersion}/report-outcomes")
            .HasApiVersion(1)
            .MapPost("/", ReportAsync)
            .WithName("ReportOutcome")
            .RequireAuthorization(policy => policy.RequireRole(ReportOutcomeRole))
            .WithSummary("Tell the Driver app what became of a Driver's report: Recorded, Refused (with the TMS's code and message) or Not recorded.")
            .WithDescription(
                "Only the identity holding the app role DriverReports.ReportOutcome may call it. A report that has an outcome already is left as it is and acknowledged "
                + "with result Ignored, so the Logic App can send an outcome again. An unknown report, or one of another Trip, is ReportNotFound (404).");

        return app;
    }

    private static async Task<IResult> ReportAsync(ReportOutcomeRequest request, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(request.ToCommand(), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(new ReportOutcomeAccepted(result.Value)),
            ResultStatus.Invalid => Results.ValidationProblem(result.ValidationErrors
                .GroupBy(error => error.Identifier)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())),
            _ => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The Driver app has no such report.",
                extensions: new Dictionary<string, object?> { ["code"] = "ReportNotFound", ["correlationId"] = request.CorrelationId }),
        };
    }
}
