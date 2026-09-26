using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.Trails.Open;
using Norvia.IntegrationConsole.Application.Trails.Search;

namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>
/// Sends the application's queries, each in a scope of its own. A Blazor Server circuit lives as long as the page is
/// open, so a scope per circuit would keep its services for all of it.
/// </summary>
internal sealed class TrailRequests(IServiceScopeFactory scopes) : ITrailRequests
{
    public Task<Result<TrailSearchResult>> SearchAsync(SearchTrailsQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<MessageTrail>> OpenAsync(OpenTrailQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    private async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
