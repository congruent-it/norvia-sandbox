using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Application.ShipperInvoices.Issue;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.UnitTests.ShipperInvoices;

public sealed class MonthlyInvoiceRunTests : IDisposable
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();
    private readonly ServiceProvider _services;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly List<IssueShipperInvoiceCommand> _sent = [];

    public MonthlyInvoiceRunTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ApplicationName.Returns("Norvia.Erp.Web");
        _services = new ServiceCollection()
            .AddSingleton(_mediator)
            .AddSingleton(_retrier)
            .AddSingleton(new ErpActivities(environment))
            .BuildServiceProvider();
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<IssueShipperInvoiceResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<IssueShipperInvoiceResult>>>>()(_mediator));
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ListShippersQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<ShipperView>>([new("SHP-0001", "A", true), new("SHP-0002", "B", true)]));
        _mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sent.Add(call.Arg<IssueShipperInvoiceCommand>());
                return ValueTask.FromResult<Result<IssueShipperInvoiceResult>>(new IssueShipperInvoiceResult(null));
            });
#pragma warning restore CA2012
    }

    public void Dispose() => _services.Dispose();

    private MonthlyInvoiceRun Run => new(_services.GetRequiredService<IServiceScopeFactory>(), _services.GetRequiredService<ErpActivities>(), NullLogger<MonthlyInvoiceRun>.Instance);

    private MonthlyInvoiceRunWorker Worker => new(Run, _time);

    [Fact]
    public async Task Run_InvoicesEveryShipperOverTheLinesDeliveredBeforeThe1st()
    {
        var succeeded = await Run.RunAsync(new DateOnly(2027, 1, 1), Xunit.TestContext.Current.CancellationToken);

        succeeded.Should().BeTrue();
        var cutoff = new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero);
        _sent.Should().Equal(new IssueShipperInvoiceCommand("SHP-0001", cutoff), new IssueShipperInvoiceCommand("SHP-0002", cutoff));
    }

    [Fact]
    public async Task Run_WhenOneShipperFails_StillInvoicesTheOthersAndSaysToRunAgain()
    {
        var calls = 0;
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<Result<IssueShipperInvoiceResult>>>(call =>
            {
                _sent.Add(call.Arg<IssueShipperInvoiceCommand>());
                return calls++ == 0 ? throw new InvalidOperationException("database down") : ValueTask.FromResult<Result<IssueShipperInvoiceResult>>(new IssueShipperInvoiceResult(null));
            });
#pragma warning restore CA2012

        var succeeded = await Run.RunAsync(new DateOnly(2026, 10, 1), Xunit.TestContext.Current.CancellationToken);

        succeeded.Should().BeFalse();
        _sent.Select(command => command.ShipperId).Should().Equal("SHP-0001", "SHP-0002");
    }

    [Fact]
    public async Task Worker_BeforeThe1stAt0600_RunsLastMonthsRunOnceAndNotAgain()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        var worker = Worker;

        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);
        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);

        _sent.Should().HaveCount(2).And.OnlyContain(command => command.DeliveredBefore == new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Worker_AtTheChangeOfYear_RunsOnlyAt0600OnThe1stOverTheOldYearsLines()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero)); // 00:30 on 1 January in Amsterdam
        var worker = Worker;
        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);
        _sent.Clear();

        _time.SetUtcNow(new DateTimeOffset(2027, 1, 1, 4, 59, 0, TimeSpan.Zero));
        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);
        _sent.Should().BeEmpty();

        _time.SetUtcNow(new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero));
        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);
        _sent.Should().HaveCount(2).And.OnlyContain(command => command.DeliveredBefore == new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Worker_WhenARunFailed_TriesAgainAtTheNextLook()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ListShippersQuery>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<ShipperView>>>(_ => throw new InvalidOperationException("database down"));
#pragma warning restore CA2012
        var worker = Worker;

        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);
        _sent.Should().BeEmpty();

#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ListShippersQuery>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<ShipperView>>([new("SHP-0001", "A", true)]));
#pragma warning restore CA2012
        await worker.RunIfDueAsync(Xunit.TestContext.Current.CancellationToken);

        _sent.Should().ContainSingle();
    }
}
