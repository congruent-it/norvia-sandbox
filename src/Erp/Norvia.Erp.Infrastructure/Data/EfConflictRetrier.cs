using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Application;

namespace Norvia.Erp.Infrastructure.Data;

/// <summary>
/// Runs a command again, in a scope of its own, when it lost a conflict with another writer. A retry can lose to a third writer, or to the
/// first one that has not committed yet, so it is tried a few times, not once. The last conflict is thrown.
/// </summary>
internal sealed class EfConflictRetrier(IMediator mediator, IServiceScopeFactory scopes) : IConflictRetrier
{
    private const int DuplicateKeyInUniqueIndex = 2601;
    private const int DuplicateKeyInConstraint = 2627;
    private const int Retries = 3;

    public async Task<TResult> RunAsync<TResult>(Func<IMediator, Task<TResult>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        try
        {
            return await send(mediator);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            for (var retry = 1; ; retry++)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
                }
                catch (Exception again) when (retry < Retries && IsConflict(again))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * retry));
                }
            }
        }
    }

    private static bool IsConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException or DbUpdateException { InnerException: SqlException { Number: DuplicateKeyInUniqueIndex or DuplicateKeyInConstraint } };
}
