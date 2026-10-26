using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Application.Runs.Execute;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>The Carriers and Finance after the deliveries (E2): each Carrier invoices what it delivered, Finance decides the lines Norvia couldn't match, and the outcomes arrive on the Carriers' pages.</summary>
public sealed class CarrierInvoicingStageTests
{
    private const decimal Price = 100m;

    private static readonly CarrierShares Everything = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 0,
        DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 0);

    private readonly IRepository<SimulationRun> _runs = Substitute.For<IRepository<SimulationRun>>();
    private readonly IRepository<LogEntry> _log = Substitute.For<IRepository<LogEntry>>();
    private readonly IPartnerApi _api = Substitute.For<IPartnerApi>();
    private readonly ITmsApi _tms = Substitute.For<ITmsApi>();
    private readonly IErpApi _erp = Substitute.For<IErpApi>();
    private readonly ICarrierInbox _inbox = Substitute.For<ICarrierInbox>();
    private readonly IWorldSource _world = Substitute.For<IWorldSource>();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly List<LogEntry> _lines = [];
    private readonly List<(SimulatedCarrier Carrier, CarrierInvoiceRequest Invoice)> _invoices = [];
    private readonly List<(string Carrier, string Invoice, string Reference, decimal? Amount, string? Remark)> _accepted = [];
    private readonly List<(string Carrier, string Invoice, string Reference, string Reason)> _rejected = [];
    private readonly Dictionary<string, decimal?> _prices = [];
    private bool _outcomesArrive = true;
    private bool _decisionsWork = true;

    public CarrierInvoicingStageTests()
    {
        _world.Load().Returns(TestData.World);
        _log.AddAsync(Arg.Do<LogEntry>(line => { lock (_lines) { _lines.Add(line); } }), Arg.Any<CancellationToken>()).Returns(call => call.Arg<LogEntry>());
        _api.SendBookingAsync(Arg.Any<PlannedBooking>(), Arg.Any<CancellationToken>())
            .Returns(call => new BookingAnswer(LogOutcome.Accepted, $"corr-{call.Arg<PlannedBooking>().Step}", $"TO-{call.Arg<PlannedBooking>().Step:0000000}", null));
        _tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FleetDay>?>(
            [.. Enumerable.Range(0, 10).Select(offset => new FleetDay(DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(offset), 100_000, 1_000, 0, 0))]));
        _tms.AssignAsync(Arg.Any<string>(), Arg.Any<Assignee>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlannerAnswer(LogOutcome.Planned, $"plan-{call.ArgAt<string>(0)}", $"SH-{call.ArgAt<string>(0)[3..]}", null));
        _tms.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PlannerAnswer(LogOutcome.Released, $"rel-{call.ArgAt<string>(0)}", call.ArgAt<string>(0), null, CarrierBookingNumber: $"CB-{call.ArgAt<string>(0)[3..]}"));
        _tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>([]));
        _api.AnswerBookingAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierAnswerRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new CarrierCallAnswer(LogOutcome.Confirmed, "ans", null));
        _api.SendTrackingAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierTrackingRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new CarrierCallAnswer(LogOutcome.TrackingSent, "trk", null));
        _api.SendProofOfDeliveryAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierProofRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new CarrierCallAnswer(LogOutcome.ProofOfDeliverySent, "pod", null));
        _erp.InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new InvoiceNowAnswer(LogOutcome.NothingToInvoice, null, null, [], null));

        // Norvia offered 100.00 for every booking; it matches a line claimed at that price and puts any other amount To decide.
        _inbox.AgreedPricesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyDictionary<string, decimal>?>(
            _prices.Count > 0 ? _prices.Where(price => price.Value is not null).ToDictionary(price => price.Key, price => price.Value!.Value) : Enumerable.Range(1, 30).ToDictionary(step => $"CB-{step:0000000}", _ => Price)));
        _api.SendCarrierInvoiceAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierInvoiceRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var invoice = call.Arg<CarrierInvoiceRequest>();
            lock (_invoices) { _invoices.Add((call.Arg<SimulatedCarrier>(), invoice)); }
            var lines = invoice.Lines.Select(line => new CarrierInvoiceLineAnswer(line.BookingReference, line.Amount, line.Amount == Price ? "MATCHED" : "PRICE_MISMATCH")).ToList();
            return new CarrierInvoiceAnswer(LogOutcome.CarrierInvoiced, $"inv-{invoice.InvoiceNumber}", lines.All(line => line.Matched) ? "APPROVED" : "RECEIVED", lines, null);
        });
        _erp.AcceptCarrierInvoiceLineAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (_accepted) { _accepted.Add((call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<decimal?>(3), call.ArgAt<string?>(4))); }
            return _decisionsWork ? new LineDecisionAnswer(LogOutcome.LineAccepted, $"dec-{call.ArgAt<string>(1)}", null) : new LineDecisionAnswer(LogOutcome.InvoicingRefused, null, "CarrierInvoiceLineAlreadyDecided (409)");
        });
        _erp.RejectCarrierInvoiceLineAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (_rejected) { _rejected.Add((call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<string>(3))); }
            return new LineDecisionAnswer(LogOutcome.LineRejected, $"dec-{call.ArgAt<string>(1)}", null);
        });
        _inbox.OutcomesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult<IReadOnlyList<CarrierInvoiceOutcome>?>(_outcomesArrive ? OutcomesFor(call.ArgAt<string>(0)) : []));
    }

    private IEnumerable<LogEntry> Stage => _lines.Where(line => LogOutcomes.Invoicing.Contains(line.Outcome) || line.Action.StartsWith("Send Carrier", StringComparison.Ordinal));

    // What Norvia tells each Carrier: MATCHED at the invoiced amount, the accepted lines adjusted to the agreed price, the rejected ones with their reason.
    private List<CarrierInvoiceOutcome> OutcomesFor(string carrierId)
    {
        lock (_invoices)
        {
            return [.. _invoices.Where(sent => sent.Carrier.Id == carrierId).Select(sent => new CarrierInvoiceOutcome(
                sent.Invoice.InvoiceNumber, $"out-{sent.Invoice.InvoiceNumber}", sent.Invoice.Lines.Sum(line => line.Amount),
                [.. sent.Invoice.Lines.Select(line =>
                {
                    if (_rejected.Any(rejected => rejected.Invoice == sent.Invoice.InvoiceNumber && rejected.Reference == line.BookingReference))
                    {
                        return new CarrierOutcomeLine(line.BookingReference, "REJECTED", 0m, "Simulated");
                    }

                    return line.Amount == Price
                        ? new CarrierOutcomeLine(line.BookingReference, "MATCHED", line.Amount, null)
                        : new CarrierOutcomeLine(line.BookingReference, "ACCEPTED_ADJUSTED", Price, "Agreed price applies");
                })]))];
        }
    }

    private ExecuteRunHandler Handler() => new(
        _runs, _log, _api, _tms, _world, new SimulationLimits(50, 3), _time, new CarrierPacing(TimeSpan.Zero, 1), Substitute.For<IDriverApp>(), new DriverPacing(TimeSpan.Zero, 2), _erp,
        Substitute.For<IShipperInbox>(), _inbox);

    private async Task<SimulationRun> RunAsync(int edited = 100, int rejected = 0, int orders = 6, CarrierShares? shares = null, CancellationTokenSource? stop = null)
    {
        var run = TestData.Run(TestData.Settings(orders: orders, assigned: 100, ownFleet: 0, carriers: shares ?? Everything) with
        {
            Invoicing = new InvoicingShares(0, 0, edited, rejected),
        });
        _runs.FirstOrDefaultAsync(Arg.Any<ISpecification<SimulationRun>>(), Arg.Any<CancellationToken>()).Returns(run);
        (await Handler().Handle(new ExecuteRunCommand(run.Id, stop?.Token ?? CancellationToken.None), CancellationToken.None)).IsSuccess.Should().BeTrue();
        return run;
    }

    [Fact]
    public async Task Handle_EveryCarrierThatDelivered_SendsOneInvoice_AsItsPersona_InIdOrder()
    {
        var run = await RunAsync(edited: 0);

        _invoices.Select(sent => sent.Carrier.Id).Should().Equal(_invoices.Select(sent => sent.Carrier.Id).Distinct().Order(StringComparer.Ordinal));
        _invoices.Should().OnlyContain(sent => sent.Invoice.InvoiceNumber == $"{sent.Carrier.BookingReferencePrefix}-SIM{run.Number}-INV");
        _invoices.Sum(sent => sent.Invoice.Lines.Count).Should().Be(6);
        _invoices.SelectMany(sent => sent.Invoice.Lines).Should().OnlyContain(line => line.Amount == Price && line.BookingReference.Contains("-SIM1-"));
        var sentLines = Stage.Where(line => line.Action == InvoicingActions.SendCarrierInvoice).ToList();
        sentLines.Should().HaveCount(_invoices.Count).And.OnlyContain(line => line.Outcome == LogOutcome.CarrierInvoiced && line.CorrelationId!.StartsWith("inv-", StringComparison.Ordinal));
        sentLines.Select(line => line.Actor).Should().Equal(_invoices.Select(sent => sent.Carrier.PersonaName));
        sentLines[0].Detail.Should().Contain("is APPROVED").And.Contain("MATCHED").And.Contain("EUR 100.00");
    }

    [Fact]
    public async Task Handle_AnEditedShare_EditsExactlyOneLineOfThatShareOfTheInvoices()
    {
        await RunAsync(edited: 100);

        _invoices.Should().OnlyContain(sent => sent.Invoice.Lines.Count(line => line.Amount != Price) == 1);
        _invoices.SelectMany(sent => sent.Invoice.Lines).Where(line => line.Amount != Price).Should().OnlyContain(line => line.Amount == 110m && line.Description == "Waiting time included");
    }

    [Fact]
    public async Task Handle_NoEditedShare_EditsNothing_AndEveryInvoiceIsApprovedAtOnce()
    {
        await RunAsync(edited: 0);

        _invoices.SelectMany(sent => sent.Invoice.Lines).Should().OnlyContain(line => line.Amount == Price);
        await _erp.DidNotReceiveWithAnyArgs().AcceptCarrierInvoiceLineAsync(default!, default!, default!, default, default, TestContext.Current.CancellationToken);
        await _erp.DidNotReceiveWithAnyArgs().RejectCarrierInvoiceLineAsync(default!, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ALineLeftToDecide_IsAcceptedAtTheAgreedAmount_WithARemark_AsFinance()
    {
        await RunAsync(edited: 100, rejected: 0);

        _accepted.Should().HaveCount(_invoices.Count).And.OnlyContain(accepted => accepted.Amount == Price && accepted.Remark!.Contains("run 1"));
        _rejected.Should().BeEmpty();
        var accepts = Stage.Where(line => line.Action == InvoicingActions.AcceptCarrierInvoiceLine).ToList();
        accepts.Should().OnlyContain(line => line.Actor == "Finance" && line.Outcome == LogOutcome.LineAccepted && line.ShipperReference != null && line.TransportOrderNumber != null);
        accepts[0].Detail.Should().Contain("claimed EUR 110.00").And.Contain("accepted at the agreed EUR 100.00");
    }

    [Fact]
    public async Task Handle_ARejectedShare_RejectsExactlyThatShareOfTheLinesLeftToDecide_WithAReason()
    {
        await RunAsync(edited: 100, rejected: 50);

        var toDecide = _invoices.Count;
        _rejected.Should().HaveCount((int)Math.Round(toDecide * 0.5, MidpointRounding.AwayFromZero));
        _accepted.Should().HaveCount(toDecide - _rejected.Count);
        _rejected.Should().OnlyContain(rejected => rejected.Reason.Contains("run 1") && rejected.Reason.Contains("PRICE_MISMATCH"));
        Stage.Where(line => line.Action == InvoicingActions.RejectCarrierInvoiceLine).Should().OnlyContain(line => line.Actor == "Finance" && line.Outcome == LogOutcome.LineRejected);
    }

    [Fact]
    public async Task Handle_AnOutcomeOnTheCarriersPage_IsLoggedPerLine_AsTheCarriersPersona_WithItsResult()
    {
        await RunAsync(edited: 100, rejected: 50);

        var outcomes = Stage.Where(line => line.Outcome == LogOutcome.OutcomeReceived).ToList();
        outcomes.Should().HaveCount(6).And.OnlyContain(line => line.Action == InvoicingActions.ReceiveInvoiceOutcome && line.CorrelationId!.StartsWith("out-", StringComparison.Ordinal));
        outcomes.Select(line => line.Detail!.Split(':')[0]).Distinct().Should().BeSubsetOf(["MATCHED", "ACCEPTED_ADJUSTED", "REJECTED"]);
        outcomes.Should().Contain(line => line.Detail!.StartsWith("REJECTED", StringComparison.Ordinal) && line.Detail.Contains("Simulated"));
        outcomes.Should().Contain(line => line.Detail!.StartsWith("ACCEPTED_ADJUSTED", StringComparison.Ordinal) && line.Detail.Contains("EUR 100.00"));
        outcomes.Select(line => line.Actor).Distinct().Should().BeSubsetOf(["Van Dijk Transport", "Kessler Spedition"]);
        _lines.Last().Action.Should().Contain($"{_invoices.Count} Carrier invoices sent").And.Contain("6 outcomes on the Carriers' pages");
    }

    [Fact]
    public async Task Handle_AnOutcomeThatNeverArrives_IsNotedAfterTheLastAsk()
    {
        _outcomesArrive = false;

        await RunAsync(edited: 0);

        Stage.Should().NotContain(line => line.Outcome == LogOutcome.OutcomeReceived);
        _lines.Where(line => line.Outcome == LogOutcome.Info && line.Action.Contains("didn't reach the invoices page")).Should().HaveCount(_invoices.Count);
    }

    [Fact]
    public async Task Handle_APageThatCantBeRead_IsNotedAndTheRunGoesOn()
    {
        _inbox.OutcomesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<CarrierInvoiceOutcome>?>(null));

        await RunAsync(edited: 0);

        _lines.Should().Contain(line => line.Action.Contains("invoices page of") && line.Action.Contains("couldn't be read"));
        _lines.Last().Action.Should().Contain("finished");
    }

    [Fact]
    public async Task Handle_AnInboxThatCantBeRead_SendsNoInvoice_AndSaysSo()
    {
        _inbox.AgreedPricesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyDictionary<string, decimal>?>(null));

        await RunAsync();

        await _api.DidNotReceiveWithAnyArgs().SendCarrierInvoiceAsync(default!, default!, TestContext.Current.CancellationToken);
        _lines.Should().Contain(line => line.Action.Contains("booking inbox of") && line.Action.Contains("no Carrier invoice"));
    }

    [Fact]
    public async Task Handle_ABookingWithoutAPrice_IsNotInvoiced_AndSaysSo()
    {
        _prices["CB-0000001"] = Price;

        await RunAsync(edited: 0);

        _invoices.SelectMany(sent => sent.Invoice.Lines).Should().ContainSingle();
        _lines.Count(line => line.Action.Contains("has no price for Carrier booking")).Should().Be(5);
    }

    [Fact]
    public async Task Handle_PricesThatReachTheInboxOnTheSecondAsk_AreInvoiced_BecauseTheInboxIsAskedAgain()
    {
        var asks = new Dictionary<string, int>();
        _inbox.AgreedPricesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var ask = asks[call.Arg<string>()] = asks.GetValueOrDefault(call.Arg<string>()) + 1;
            return Task.FromResult<IReadOnlyDictionary<string, decimal>?>(
                ask == 1 ? new Dictionary<string, decimal>() : Enumerable.Range(1, 30).ToDictionary(step => $"CB-{step:0000000}", _ => Price));
        });

        await RunAsync(edited: 0);

        _invoices.Sum(sent => sent.Invoice.Lines.Count).Should().Be(6);
        _lines.Should().NotContain(line => line.Action.Contains("has no price for Carrier booking"));
    }

    [Fact]
    public async Task Handle_NorviaRefusingTheInvoice_IsLogged_AndNothingMoreHappensForIt()
    {
        _api.SendCarrierInvoiceAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierInvoiceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierInvoiceAnswer(LogOutcome.CarrierRefused, "ref-1", null, [], "INVOICE_NUMBER_USED (409)"));

        await RunAsync();

        Stage.Where(line => line.Action == InvoicingActions.SendCarrierInvoice).Should().OnlyContain(line => line.Outcome == LogOutcome.CarrierRefused && line.Detail!.Contains("INVOICE_NUMBER_USED (409)"));
        await _erp.DidNotReceiveWithAnyArgs().AcceptCarrierInvoiceLineAsync(default!, default!, default!, default, default, TestContext.Current.CancellationToken);
        Stage.Should().NotContain(line => line.Outcome == LogOutcome.OutcomeReceived);
        _lines.Last().Action.Should().Contain("finished").And.NotContain("Carrier invoices sent");
    }

    [Fact]
    public async Task Handle_ARefusedDecision_IsLoggedAsTheErpsRefusal_AndTheInvoiceIsNotWaitedFor()
    {
        _decisionsWork = false;

        await RunAsync(edited: 100, rejected: 0);

        Stage.Where(line => line.Action == InvoicingActions.AcceptCarrierInvoiceLine).Should().OnlyContain(line => line.Outcome == LogOutcome.InvoicingRefused && line.Detail!.Contains("CarrierInvoiceLineAlreadyDecided (409)"));
        Stage.Should().NotContain(line => line.Outcome == LogOutcome.OutcomeReceived);
        await _inbox.DidNotReceiveWithAnyArgs().OutcomesAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_AResultOtherThanAPriceMismatch_IsRejected_BecauseThereIsNothingToAcceptItAgainst()
    {
        _api.SendCarrierInvoiceAsync(Arg.Any<SimulatedCarrier>(), Arg.Any<CarrierInvoiceRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var invoice = call.Arg<CarrierInvoiceRequest>();
            lock (_invoices) { _invoices.Add((call.Arg<SimulatedCarrier>(), invoice)); }
            return new CarrierInvoiceAnswer(
                LogOutcome.CarrierInvoiced, "inv", "RECEIVED", [.. invoice.Lines.Select(line => new CarrierInvoiceLineAnswer(line.BookingReference, line.Amount, "UNKNOWN_BOOKING"))], null);
        });

        await RunAsync(edited: 0, rejected: 0);

        _accepted.Should().BeEmpty();
        _rejected.Should().HaveCount(6).And.OnlyContain(rejected => rejected.Reason.Contains("UNKNOWN_BOOKING"));
    }

    [Fact]
    public async Task Handle_NothingDeliveredByACarrier_RunsNoCarrierInvoicing()
    {
        await RunAsync(shares: Everything with { DeliveredPercent = 0 });

        await _api.DidNotReceiveWithAnyArgs().SendCarrierInvoiceAsync(default!, default!, TestContext.Current.CancellationToken);
        await _inbox.DidNotReceiveWithAnyArgs().AgreedPricesAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_TheSameSeed_GivesTheSameInvoicesAndDecisions()
    {
        await RunAsync(edited: 50, rejected: 50, orders: 8);
        var first = (_invoices.SelectMany(sent => sent.Invoice.Lines.Select(line => line.Amount)).ToList(), _rejected.Select(rejected => rejected.Reference).ToList());
        _invoices.Clear();
        _rejected.Clear();
        _accepted.Clear();
        _lines.Clear();

        await RunAsync(edited: 50, rejected: 50, orders: 8);

        (_invoices.SelectMany(sent => sent.Invoice.Lines.Select(line => line.Amount)).ToList(), _rejected.Select(rejected => rejected.Reference).ToList()).Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task Handle_ARunCancelledBeforeTheCarriersInvoice_SendsNothing()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await RunAsync(stop: stop);

        await _api.DidNotReceiveWithAnyArgs().SendCarrierInvoiceAsync(default!, default!, TestContext.Current.CancellationToken);
    }
}
