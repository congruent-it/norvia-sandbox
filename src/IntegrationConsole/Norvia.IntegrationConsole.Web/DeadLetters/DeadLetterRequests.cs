using Ardalis.Result;
using Mediator;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.DeadLetters.List;
using Norvia.IntegrationConsole.Application.DeadLetters.Messages;
using Norvia.IntegrationConsole.Application.DeadLetters.Resubmit;

namespace Norvia.IntegrationConsole.Web.DeadLetters;

/// <summary>Sends the application's queries, each in a scope of its own (see RunRequests).</summary>
internal sealed class DeadLetterRequests(IServiceScopeFactory scopes) : IDeadLetterRequests
{
    public Task<DeadLetterQueueList> ListAsync(ListDeadLetterQueuesQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<DeadLetteredMessageList>> ListMessagesAsync(ListDeadLetteredMessagesQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public Task<Result<RecordedDeadLetterBody>> OpenMessageAsync(OpenDeadLetteredMessageQuery query, CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    public async Task<Result<ResubmittedDeadLetter>> ResubmitAsync(ResubmitDeadLetteredMessageCommand command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command, cancellationToken);
    }

    private async Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }
}
