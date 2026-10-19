using Ardalis.Result;
using Bunit.TestDoubles;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.Operators;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Web.CarrierInvoices;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests;

/// <summary>Renders the ERP's components with Fluent UI in place and a user the test signs in, holding the Finance role.</summary>
public abstract class ErpWebContext : BunitContext
{
    protected ErpWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Mediator);
        Services.AddSingleton(Retrier);
        Services.AddSingleton(new ErpActivities(HostEnvironment));
        Services.AddScoped<InvoiceNowAction>();
        Services.AddScoped<SendAgainAction>();
        Services.AddScoped<IssueCreditNoteAction>();
        Services.AddScoped<DecideCarrierInvoiceLineAction>();
        User = AddAuthorization();
        User.SetAuthorized("Dev Eloper").SetRoles(ErpRoles.Finance);
        User.SetPolicies(ErpRoles.Finance);
        Retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<IssueShipperInvoiceResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<IssueShipperInvoiceResult>>>>()(Mediator));
        Retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<CreditNoteView>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<CreditNoteView>>>>()(Mediator));
        Retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<CarrierInvoiceView>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<CarrierInvoiceView>>>>()(Mediator));
        Retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<ShipperInvoiceView>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<ShipperInvoiceView>>>>()(Mediator));
    }

    private static IHostEnvironment HostEnvironmentWithName()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Erp.Web");
        return environment;
    }

    protected BunitAuthorizationContext User { get; }

    /// <summary>Runs what it is given on the substituted mediator, as the real one does once and without a conflict.</summary>
    protected IConflictRetrier Retrier { get; } = Substitute.For<IConflictRetrier>();

    private static IHostEnvironment HostEnvironment { get; } = HostEnvironmentWithName();

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
