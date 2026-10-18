using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application;

namespace Norvia.Erp.Infrastructure.Data;

internal sealed class EfConflictRetrier(IMediator mediator, IServiceScopeFactory scopes) : IConflictRetrier
{
    private const int DuplicateKeyInUniqueIndex = 2601;
    private const int DuplicateKeyInConstraint = 2627;

    public async Task<TResult> RunAsync<TResult>(Func<IMediator, Task<TResult>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        try
        {
            return await send(mediator);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException { InnerException: SqlException { Number: DuplicateKeyInUniqueIndex or DuplicateKeyInConstraint } })
        {
            await using var scope = scopes.CreateAsyncScope();
            return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
        }
    }
}
