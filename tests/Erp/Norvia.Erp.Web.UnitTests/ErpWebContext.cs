using Bunit.TestDoubles;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Erp.Application.Operators;

namespace Norvia.Erp.Web.UnitTests;

/// <summary>Renders the ERP's components with Fluent UI in place and a user the test signs in, holding the Finance role.</summary>
public abstract class ErpWebContext : BunitContext
{
    protected ErpWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Mediator);
        User = AddAuthorization();
        User.SetAuthorized("Dev Eloper").SetRoles(ErpRoles.Finance);
        User.SetPolicies(ErpRoles.Finance);
    }

    protected BunitAuthorizationContext User { get; }

    protected IMediator Mediator { get; } = Substitute.For<IMediator>();

    /// <summary>Has the mediator answer a query.</summary>
    protected void Answer<TQuery, TResponse>(TResponse response)
        where TQuery : class, IQuery<TResponse>
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<TQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(response));
#pragma warning restore CA2012
    }

    /// <summary>Has the mediator fail on a query.</summary>
    protected void Fail<TQuery, TResponse>(Exception exception)
        where TQuery : class, IQuery<TResponse>
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        Mediator.Send(Arg.Any<TQuery>(), Arg.Any<CancellationToken>()).Returns<ValueTask<TResponse>>(_ => throw exception);
#pragma warning restore CA2012
    }
}
