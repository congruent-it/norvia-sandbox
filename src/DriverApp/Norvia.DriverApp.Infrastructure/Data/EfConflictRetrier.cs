using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.DriverApp.Application;

namespace Norvia.DriverApp.Infrastructure.Data;

internal sealed class EfConflictRetrier(IMediator mediator, IServiceScopeFactory scopes) : IConflictRetrier
{
    // Each conflict means another call stored the Trip, so a call loses at most once to each of the others sent at once.
    private const int MaxAttempts = 10;

    public async Task<TResult> RunAsync<TResult>(Func<IMediator, Task<TResult>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        try
        {
            return await send(mediator);
        }
        catch (DbUpdateException)
        {
            // Each retry reads the Trip as the winner left it. The last attempt's failure, of whatever cause, is thrown.
            for (var attempt = 2; ; attempt++)
            {
                await using var scope = scopes.CreateAsyncScope();
                try
                {
                    return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
                }
                catch (DbUpdateException) when (attempt < MaxAttempts)
                {
                }
            }
        }
    }
}
