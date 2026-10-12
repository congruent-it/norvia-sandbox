namespace Norvia.DriverApp.Application;

/// <summary>
/// Runs a call and, when another call to store the same Trip won the race and the save conflicted, runs it again, a few
/// times at most, each in a scope of its own, which reads what the winner stored. So a version that arrives twice at once, or a lower one that
/// loses the race to a higher one, is acknowledged and ignored, not answered with an error.
/// </summary>
public interface IConflictRetrier
{
    Task<TResult> RunAsync<TResult>(Func<Mediator.IMediator, Task<TResult>> send);
}
