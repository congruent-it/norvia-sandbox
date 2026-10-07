using Mediator;
using Norvia.PartnerPortal.Application;

namespace Norvia.PartnerPortal.Web.UnitTests;

/// <summary>Runs the call against the mediator the test gives it, once: the endpoint tests don't race, and the retrying has its own tests.</summary>
internal sealed class PassThroughRetrier(IMediator mediator) : IConflictRetrier
{
    public Task<TResult> RunAsync<TResult>(Func<IMediator, Task<TResult>> send) => send(mediator);
}
