namespace Norvia.Erp.Application;

/// <summary>
/// Runs a call and, when another call recorded the same thing first and the save conflicted, runs it once more in a scope of its own,
/// which reads what the winner left. So two calls for the same Transport Order answer a record and a repeat, not an error.
/// </summary>
public interface IConflictRetrier
{
    Task<TResult> RunAsync<TResult>(Func<Mediator.IMediator, Task<TResult>> send);
}
