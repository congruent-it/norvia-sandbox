namespace Norvia.Tms.Application;

/// <summary>
/// Runs a call and, when another change to the same data won the race and the save conflicted, runs it once more in a scope of its
/// own, which reads the data as the winner left it. So a report for something that is no longer in the state it reports on is
/// ignored, rather than answered with an error.
/// </summary>
public interface IConflictRetrier
{
    Task<TResult> RunAsync<TResult>(Func<Mediator.IMediator, Task<TResult>> send);
}
