using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.Runs.Open;
using Norvia.IntegrationConsole.Application.Runs.Steps;

namespace Norvia.IntegrationConsole.Web.Runs;

/// <summary>
/// Sends the application's queries, each in a scope of its own. A Blazor Server circuit lives as long as the page is
/// open, so a scope per circuit would keep its services for all of it.
/// </summary>
internal sealed class RunRequests(IServiceScopeFactory scopes) : IRunRequests
{
    public Task<Result<RunDetail>> OpenAsync(OpenRunQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<StepDetail>> OpenStepAsync(OpenStepQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    private async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
