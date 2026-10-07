namespace Norvia.PartnerPortal.Application;

/// <summary>
/// Runs a call and, when another call to store the same thing won the race and the save conflicted on its key, runs it once
/// more in a scope of its own, which reads what the winner stored. So a message that arrives twice at once is acknowledged
/// as a repeat, not answered with an error.
/// </summary>
public interface IConflictRetrier
{
    Task<TResult> RunAsync<TResult>(Func<Mediator.IMediator, Task<TResult>> send);
}
