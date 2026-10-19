using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>Finance after the deliveries, through the ERP API: Invoice now, a partial Credit note, and the documents arriving in the Shippers' inboxes.</summary>
public sealed class InvoicingStageTests
{
    private readonly FleetRun _fleet = new();
    private readonly List<InboxDocument> _inbox = [];
    private readonly List<(string Shipper, string Invoice)> _invoiced = [];
    private int _invoices;
    private bool _arrives;

    public InvoicingStageTests()
    {
        _fleet.Erp.InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var number = $"INV-2026-{++_invoices:000000}";
            _invoiced.Add((call.ArgAt<string>(0), number));
            return new InvoiceNowAnswer(
                LogOutcome.InvoiceIssued, $"inv-{number}", number, [new InvoicedLine($"TO-{_invoices:0000000}", $"SIM-1-{_invoices}", 80m), new InvoicedLine("TO-9999999", "SIM-1-99", 40m)], null, 120m);
        });
        _fleet.Erp.IssueCreditNoteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new CreditNoteAnswer(LogOutcome.CreditNoteIssued, $"cn-{call.ArgAt<string>(0)}", $"CN-{call.ArgAt<string>(0)[4..]}", null));
        _fleet.Inbox.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<InboxDocument>?>([.. _inbox, .. Arrived()]));
    }

    private IEnumerable<LogEntry> Invoicing => _fleet.Lines.Where(line => LogOutcomes.Invoicing.Contains(line.Outcome));

    private static SimulationSettings Settings(int invoice = 100, int credit = 100, int today = 100) =>
        FleetRun.Settings(todayPercent: today, invoicing: new InvoicingShares(invoice, credit));

    // Once the Shipper invoice Logic App delivers, everything the ERP issued is in the inbox.
    private IEnumerable<InboxDocument> Arrived() => !_arrives
        ? []
        : _invoiced.SelectMany(pair => new[]
        {
            new InboxDocument(pair.Invoice, "INVOICE", $"inv-{pair.Invoice}"),
            new InboxDocument($"CN-{pair.Invoice[4..]}", "CREDIT_NOTE", $"cn-{pair.Invoice}"),
        });

    [Fact]
    public async Task Handle_TheDeliveriesAreIn_FinanceInvoicesEachShipperOfTheRun_OneAtATimeInIdOrder()
    {
        _arrives = true;
        var run = await _fleet.RunAsync(Settings());

        var shippers = run.Plan.Bookings.Select(booking => booking.ShipperId).Distinct().Order(StringComparer.Ordinal).ToList();
        _invoiced.Select(pair => pair.Shipper).Should().Equal(shippers);
        var lines = Invoicing.Where(line => line.Action == InvoicingActions.InvoiceNow).ToList();
        lines.Should().HaveCount(shippers.Count).And.OnlyContain(line => line.Actor == "Finance" && line.Outcome == LogOutcome.InvoiceIssued);
        lines[0].CorrelationId.Should().Be("inv-INV-2026-000001");
        lines[0].ShipperReference.Should().Be("SIM-1-1");
        lines[0].Detail.Should().Contain("INV-2026-000001").And.Contain("2 lines").And.Contain("EUR 120.00");
    }

    [Fact]
    public async Task Handle_AShipperCredited_GetsAPartialCreditNoteOnTheFirstLine_WithAReason()
    {
        await _fleet.RunAsync(Settings());

        await _fleet.Erp.Received().IssueCreditNoteAsync("INV-2026-000001", "TO-0000001", 20.00m, Arg.Is<string>(reason => reason.Contains("run 1")), Arg.Any<CancellationToken>());
        var credit = Invoicing.First(line => line.Action == InvoicingActions.IssueCreditNote);
        credit.Actor.Should().Be("Finance");
        credit.Outcome.Should().Be(LogOutcome.CreditNoteIssued);
        credit.CorrelationId.Should().Be("cn-INV-2026-000001");
        credit.TransportOrderNumber.Should().Be("TO-0000001");
        credit.ShipperReference.Should().Be("SIM-1-1");
        credit.Detail.Should().Contain("CN-2026-000001 on INV-2026-000001").And.Contain("EUR 20.00 of EUR 80.00");
    }

    [Fact]
    public async Task Handle_NoCreditShare_IssuesNoCreditNote()
    {
        await _fleet.RunAsync(Settings(credit: 0));

        await _fleet.Erp.DidNotReceiveWithAnyArgs().IssueCreditNoteAsync(default!, default!, default, default!, TestContext.Current.CancellationToken);
        Invoicing.Should().NotContain(line => line.Outcome == LogOutcome.CreditNoteIssued);
    }

    [Fact]
    public async Task Handle_TheDocumentsInTheInbox_AreLoggedAsTheyArrive_AsTheShippersPersona()
    {
        _arrives = true;
        var run = await _fleet.RunAsync(Settings());

        var arrived = Invoicing.Where(line => line.Outcome == LogOutcome.InboxReceived).ToList();
        arrived.Should().HaveCount(_invoiced.Count * 2);
        var first = run.Plan.Bookings.First(booking => booking.ShipperId == _invoiced[0].Shipper);
        arrived.Should().Contain(line => line.Actor == first.PersonaName && line.Action == InvoicingActions.ReceiveInvoice && line.CorrelationId == "inv-INV-2026-000001");
        arrived.Should().Contain(line => line.Actor == first.PersonaName && line.Action == InvoicingActions.ReceiveCreditNote && line.CorrelationId == "cn-INV-2026-000001"
            && line.Detail!.Contains("CN-2026-000001"));
        _fleet.Lines.Last().Action.Should().Contain($"{_invoiced.Count} invoices and {_invoiced.Count} Credit notes").And.Contain($"{_invoiced.Count * 2} in the Shippers' inboxes");
    }

    [Fact]
    public async Task Handle_ADocumentThatArrivesLater_IsFoundOnALaterAsk()
    {
        _fleet.Inbox.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<IReadOnlyList<InboxDocument>?>([]),
            _ => Task.FromResult<IReadOnlyList<InboxDocument>?>([.. _invoiced.SelectMany(pair => new[]
            {
                new InboxDocument(pair.Invoice, "INVOICE", null), new InboxDocument($"CN-{pair.Invoice[4..]}", "CREDIT_NOTE", null),
            })]));

        await _fleet.RunAsync(Settings());

        Invoicing.Count(line => line.Outcome == LogOutcome.InboxReceived).Should().BeGreaterThan(0);
        _fleet.Lines.Should().NotContain(line => line.Action.Contains("didn't reach"));
    }

    [Fact]
    public async Task Handle_ADocumentThatNeverArrives_IsNotedAfterTheLastAsk()
    {
        await _fleet.RunAsync(Settings());

        _fleet.Lines.Where(line => line.Outcome == LogOutcome.Info && line.Action.Contains("didn't reach the invoices inbox")).Should().HaveCount(_invoiced.Count);
        Invoicing.Should().NotContain(line => line.Outcome == LogOutcome.InboxReceived);
    }

    [Fact]
    public async Task Handle_AnInboxThatCantBeRead_IsNotedAndTheRunGoesOn()
    {
        _fleet.Inbox.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<InboxDocument>?>(null));

        await _fleet.RunAsync(Settings());

        _fleet.Lines.Should().Contain(line => line.Action.Contains("couldn't be read"));
        _fleet.Lines.Last().Action.Should().Contain("finished");
    }

    [Fact]
    public async Task Handle_NothingWaiting_IsAskedAgainAndLoggedOnce_WithoutACreditNote()
    {
        _fleet.Erp.InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new InvoiceNowAnswer(LogOutcome.NothingToInvoice, null, null, [], null));

        var run = await _fleet.RunAsync(Settings());

        var shippers = run.Plan.Bookings.Select(booking => booking.ShipperId).Distinct().Count();
        await _fleet.Erp.Received(shippers * 2).InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Invoicing.Should().OnlyContain(line => line.Outcome == LogOutcome.NothingToInvoice).And.HaveCount(shippers);
        Invoicing.First().Detail.Should().Contain("No Waiting lines").And.Contain("after 2 tries");
        await _fleet.Erp.DidNotReceiveWithAnyArgs().IssueCreditNoteAsync(default!, default!, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_InvoiceNowRefusedByTheErp_IsLoggedWithItsCode_AndFinanceMovesOn()
    {
        _fleet.Erp.InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new InvoiceNowAnswer(LogOutcome.InvoicingRefused, null, null, [], "ShipperNotFound (404)"));

        await _fleet.RunAsync(Settings());

        Invoicing.Should().OnlyContain(line => line.Outcome == LogOutcome.InvoicingRefused && line.Detail!.Contains("ShipperNotFound (404)"));
        await _fleet.Erp.DidNotReceiveWithAnyArgs().IssueCreditNoteAsync(default!, default!, default, default!, TestContext.Current.CancellationToken);
        await _fleet.Erp.ReceivedWithAnyArgs(Invoicing.Count()).InvoiceNowAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ACreditNoteTheErpRefuses_IsLoggedAndTheInvoiceStillWatched()
    {
        _fleet.Erp.IssueCreditNoteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreditNoteAnswer(LogOutcome.InvoicingRefused, null, null, "CreditNoteAboveLineTotal (422)"));
        foreach (var number in new[] { "INV-2026-000001", "INV-2026-000002" })
        {
            _inbox.Add(new InboxDocument(number, "INVOICE", $"inv-{number}"));
        }

        await _fleet.RunAsync(Settings());

        Invoicing.Should().Contain(line => line.Action == InvoicingActions.IssueCreditNote && line.Outcome == LogOutcome.InvoicingRefused && line.Detail!.Contains("CreditNoteAboveLineTotal (422)"));
        Invoicing.Where(line => line.Outcome == LogOutcome.InboxReceived).Should().OnlyContain(line => line.Action == InvoicingActions.ReceiveInvoice);
        _fleet.Lines.Should().NotContain(line => line.Action.Contains("CN-"));
    }

    [Fact]
    public async Task Handle_AnErpThatCantBeReached_IsLogged()
    {
        _fleet.Erp.InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new InvoiceNowAnswer(LogOutcome.InvoicingNotReceived, null, null, [], "The call didn't reach the ERP API."));

        await _fleet.RunAsync(Settings());

        Invoicing.Should().OnlyContain(line => line.Outcome == LogOutcome.InvoicingNotReceived);
    }

    [Fact]
    public async Task Handle_OnlyTheShareOfShippersAsked_IsInvoiced()
    {
        var run = await _fleet.RunAsync(Settings(invoice: 50, credit: 0));

        var shippers = run.Plan.Bookings.Select(booking => booking.ShipperId).Distinct().Count();
        _invoiced.Should().HaveCount((int)Math.Round(shippers / 2.0, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task Handle_NoInvoiceShare_CallsNeitherTheErpNorTheInboxes()
    {
        await _fleet.RunAsync(Settings(invoice: 0));

        await _fleet.Erp.DidNotReceiveWithAnyArgs().InvoiceNowAsync(default!, TestContext.Current.CancellationToken);
        await _fleet.Inbox.DidNotReceiveWithAnyArgs().ListAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_NothingWasDelivered_FinanceHasNothingToInvoice()
    {
        // The Trips are for later days, so no Driver delivers.
        await _fleet.RunAsync(Settings(today: 0));

        await _fleet.Erp.DidNotReceiveWithAnyArgs().InvoiceNowAsync(default!, TestContext.Current.CancellationToken);
        _fleet.Lines.Last().Action.Should().NotContain("invoices and");
    }

    [Fact]
    public async Task Handle_ARunCancelledWhileFinanceWorks_StopsAtOnce()
    {
        using var stop = new CancellationTokenSource();
        _fleet.Erp.InvoiceNowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return new InvoiceNowAnswer(LogOutcome.InvoiceIssued, "inv-1", "INV-2026-000001", [new InvoicedLine("TO-0000001", "SIM-1-1", 80m)], null, 80m);
        });

        await _fleet.RunAsync(Settings(), stop);

        await _fleet.Erp.ReceivedWithAnyArgs(1).InvoiceNowAsync(default!, TestContext.Current.CancellationToken);
        _fleet.Lines.Last().Action.Should().Be("Run cancelled: nothing more is sent");
    }
}
