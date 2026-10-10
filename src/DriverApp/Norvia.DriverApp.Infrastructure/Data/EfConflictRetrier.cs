using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.DriverApp.Application;

namespace Norvia.DriverApp.Infrastructure.Data;

internal sealed class EfConflictRetrier(IMediator mediator, IServiceScopeFactory scopes) : IConflictRetrier
{
    public async Task<TResult> RunAsync<TResult>(Func<IMediator, Task<TResult>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        try
        {
            return await send(mediator);
        }
        catch (DbUpdateException)
        {
            // The one retry reads the Trip as the winner left it. A failure with another cause fails again, and is thrown.
            await using var scope = scopes.CreateAsyncScope();
            return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
        }
    }
}
