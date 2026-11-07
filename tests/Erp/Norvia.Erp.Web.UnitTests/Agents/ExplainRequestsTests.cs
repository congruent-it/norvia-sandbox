using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Agents;
using Norvia.Erp.Application.Agents.Explain;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.Agents;

namespace Norvia.Erp.Web.UnitTests.Agents;

public sealed class ExplainRequestsTests
{
    [Fact]
    public async Task ExplainAsync_SendsTheQueryThroughTheMediatorOfAScopeOfItsOwn()
    {
        var query = new ExplainInvoiceLineQuery(new CarrierId("CAR-0001"), "VDT-1", "TRX-1");
        var suggestion = new Suggestion("c", [new Evidence("e", "r1", "get_invoice_line")], "a", Confidence.High);
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(query, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<Suggestion>.Success(suggestion)));
#pragma warning restore CA2012
        await using var services = new ServiceCollection().AddScoped(_ => mediator).BuildServiceProvider();

        var result = await new ExplainRequests(services.GetRequiredService<IServiceScopeFactory>()).ExplainAsync(query, Xunit.TestContext.Current.CancellationToken);

        result.Value.Should().Be(suggestion);
    }

    [Fact]
    public async Task ExplainAsync_OfAWholeInvoice_SendsTheQueryThroughTheMediatorOfAScopeOfItsOwn()
    {
        var query = new ExplainInvoiceQuery(new CarrierId("CAR-0001"), "VDT-1");
        var suggestion = new Suggestion("c", [new Evidence("e", "r1", "get_invoice_summary")], "a", Confidence.High);
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(query, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result<Suggestion>.Success(suggestion)));
#pragma warning restore CA2012
        await using var services = new ServiceCollection().AddScoped(_ => mediator).BuildServiceProvider();

        var result = await new ExplainRequests(services.GetRequiredService<IServiceScopeFactory>()).ExplainAsync(query, Xunit.TestContext.Current.CancellationToken);

        result.Value.Should().Be(suggestion);
    }
}
