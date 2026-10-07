using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application;

namespace Norvia.PartnerPortal.Infrastructure.Data;

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
            // The one retry reads the inbox as the winner left it. A failure with another cause fails again, and is thrown.
            await using var scope = scopes.CreateAsyncScope();
            return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
        }
    }
}
