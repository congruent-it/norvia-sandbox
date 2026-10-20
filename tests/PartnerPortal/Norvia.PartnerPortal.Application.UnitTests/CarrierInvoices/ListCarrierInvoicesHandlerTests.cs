using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.CarrierInvoices.List;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

public sealed class ListCarrierInvoicesHandlerTests
{
    private readonly IReadRepository<CarrierInvoice> _invoices = Substitute.For<IReadRepository<CarrierInvoice>>();
    private readonly IReadRepository<CarrierInvoiceOutcome> _outcomes = Substitute.For<IReadRepository<CarrierInvoiceOutcome>>();

    private Task<IReadOnlyList<Application.CarrierInvoices.CarrierInvoiceView>> ListAsync() =>
        new ListCarrierInvoicesHandler(_invoices, _outcomes).Handle(new ListCarrierInvoicesQuery(CarrierInvoiceSamples.Persona), CancellationToken.None).AsTask();

    [Fact]
    public async Task Handle_AnInvoiceNotToldYet_ShowsWhatNorviaMadeOfEachLine_AndNoOutcome()
    {
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([CarrierInvoiceSamples.Invoice("INV-1", "VDT-1", "VDT-2")]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var invoice = (await ListAsync()).Should().ContainSingle().Subject;

        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.OutcomeTold.Should().BeFalse();
        invoice.Total.Should().Be(200m);
        invoice.Lines.Should().OnlyContain(line => line.Result == CarrierInvoiceLineResult.PriceMismatch && line.Outcome == null);
    }

    [Fact]
    public async Task Handle_AnInvoiceToldTheOutcome_ShowsTheOutcomePerLine_AndIsApproved()
    {
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Invoice("INV-1", "VDT-1", "VDT-2"), CarrierInvoiceSamples.Invoice("INV-2", "VDT-3")]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Accepted), ("VDT-2", CarrierInvoiceOutcomeResult.Rejected))]);

        var invoices = await ListAsync();

        var told = invoices[0];
        told.Status.Should().Be(CarrierInvoiceStatus.Approved);
        told.OutcomeTold.Should().BeTrue();
        told.ApprovedAmount.Should().Be(100m);
        told.OutcomeReceivedAt.Should().Be(Samples.Now);
        told.Lines.Select(line => line.Outcome!.Result).Should().Equal(CarrierInvoiceOutcomeResult.Accepted, CarrierInvoiceOutcomeResult.Rejected);
        invoices[1].OutcomeTold.Should().BeFalse("another invoice number was not told");
    }

    [Fact]
    public async Task Handle_AnOutcomeOfAnInvoiceThePortalNeverSaw_IsListedByItsOutcomeAlone_AmongTheOthersLatestFirst()
    {
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([CarrierInvoiceSamples.Invoice("INV-1", "VDT-1")]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns([CarrierInvoiceSamples.Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Accepted)), CarrierInvoiceSamples.Outcome("SIM-INV", ("SIM-1", CarrierInvoiceOutcomeResult.Matched))]);

        var invoices = await ListAsync();

        invoices.Select(invoice => invoice.InvoiceNumber).Should().BeEquivalentTo(["INV-1", "SIM-INV"]);
        var elsewhere = invoices.Single(invoice => invoice.InvoiceNumber == "SIM-INV");
        elsewhere.SentFromPortal.Should().BeFalse();
        (elsewhere.InvoiceDate, elsewhere.Total, elsewhere.Status, elsewhere.ApprovedAmount, elsewhere.OutcomeTold).Should().Be((null, null, CarrierInvoiceStatus.Approved, 100m, true));
        elsewhere.Lines.Should().ContainSingle().Which.Should().Match<Application.CarrierInvoices.CarrierInvoiceLineView>(line => line.BookingRef == "SIM-1" && line.Amount == null && line.Result == null && line.Outcome!.Result == CarrierInvoiceOutcomeResult.Matched);
        invoices.Single(invoice => invoice.InvoiceNumber == "INV-1").SentFromPortal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NothingSentAndNothingToldYet_ListsNothing()
    {
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        (await ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheLatestFirst_WhetherSentFromThePortalOrNot()
    {
        var older = CarrierInvoiceSamples.Invoice("INV-OLD", "VDT-1");
        _invoices.ListAsync(Arg.Any<CarrierInvoicesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([older]);
        var later = CarrierInvoiceOutcome.Receive(
            CarrierInvoiceSamples.Persona, "SIM-INV", "corr-3", UnitTests.Samples.Now.AddDays(1), 10m, [new CarrierInvoiceOutcomeLine("SIM-1", CarrierInvoiceOutcomeResult.Matched, 10m, null)]);
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([later]);

        (await ListAsync()).Select(invoice => invoice.InvoiceNumber).Should().Equal("SIM-INV", "INV-OLD");
    }
}
