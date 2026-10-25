using Ardalis.Result;
using Mediator;
using Norvia.Agents;
using Norvia.HealthCheck.Application.Narrate;
using Norvia.HealthCheck.Application.Run;

namespace Norvia.HealthCheck.Web.Checks;

/// <summary>Sends a query in a scope of its own (a circuit lives as long as the page is open).</summary>
internal sealed class HealthCheckRequests(IServiceScopeFactory scopes) : IHealthCheckRequests
{
    public Task<Result<HealthCheckReport>> RunAsync(RunHealthCheckQuery query, CancellationToken cancellationToken) => SendAsync(query, cancellationToken);

    public Task<Result<Narration>> NarrateAsync(NarrateHealthCheckQuery query, CancellationToken cancellationToken) => SendAsync(query, cancellationToken);

    private async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
