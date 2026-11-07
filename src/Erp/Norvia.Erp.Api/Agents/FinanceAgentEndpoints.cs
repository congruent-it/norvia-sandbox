using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.Tracking;
using Norvia.Erp.Application.Agents;
using Norvia.Erp.Application.CarrierInvoices.AgentView;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.Agents;

/// <summary>
/// The Carrier invoice matching agent's view of the ERP (ADR 0040): a narrower read of a Carrier invoice line, which an MCP server
/// exposes as the agent's one tool (contracts/agent-mcp/finance-matching.tools.json). Reads only; there is no write endpoint. Behind a
/// token and the one role <see cref="AgentRoles.FinanceRead"/>, which only the agent's identity holds.
/// </summary>
public static class FinanceAgentEndpoints
{
    /// <summary>The <c>flow</c> of a call on the agent view, as the MCP stand-in names the server's.</summary>
    public const string Flow = "agent-view-finance-matching";

    public static IEndpointRouteBuilder MapFinanceAgent(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.NewVersionedApi("FinanceAgent")
            .MapGroup("/v{version:apiVersion}/agent/finance")
            .HasApiVersion(1)
            .RequireAuthorization(policy => policy.RequireRole(AgentRoles.FinanceRead))
            .WithMetadata(new TrackedFlow(Flow))
            .AddEndpointFilter<AgentCallTracking>();

        group.MapGet("/carriers/{carrierId}/invoices/{invoiceNumber}/lines/{carrierBookingReference}", GetInvoiceLineAsync)
            .WithName("AgentGetInvoiceLine")
            .WithSummary("Read a Carrier invoice line as the Carrier invoice matching agent: its result, the amounts, the tolerance, and the candidates or the earlier line.");
        return app;
    }

    internal static async Task<Results<Ok<AgentInvoiceLineView>, ProblemHttpResult>> GetInvoiceLineAsync(
        string carrierId, string invoiceNumber, string carrierBookingReference, IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetAgentInvoiceLineQuery(new CarrierId(carrierId), invoiceNumber, carrierBookingReference), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Carrier invoice line.");
    }
}
