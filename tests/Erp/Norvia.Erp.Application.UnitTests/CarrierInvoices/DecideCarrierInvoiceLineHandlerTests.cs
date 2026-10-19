using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.CarrierInvoices;

public sealed class DecideCarrierInvoiceLineHandlerTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private static readonly CarrierId Carrier = new("CAR-0001");

    private readonly IRepository<CarrierInvoice> _invoices = Substitute.For<IRepository<CarrierInvoice>>();
    private readonly IReadRepository<CarrierCost> _costs = Substitute.For<IReadRepository<CarrierCost>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private AcceptCarrierInvoiceLineHandler Accept => new(_invoices, _costs, _outbox, Time, NullLogger<AcceptCarrierInvoiceLineHandler>.Instance);

    private RejectCarrierInvoiceLineHandler Reject => new(_invoices, _outbox, Time, NullLogger<RejectCarrierInvoiceLineHandler>.Instance);

    private static CarrierCost Cost(string number, string reference, decimal total) =>
        CarrierCost.Record(
            new CarrierCostData(
                number, reference, Carrier, "SH-0000001", "TO-0000001",
                new CarrierCostCharge(total, [new ChargeLine(ChargeLineType.Freight, total)]), CarrierCostSource.FromSpot("remark"), Time.GetUtcNow(), "delivered"),
            Time.GetUtcNow()).Cost!;

    // TRX-1 is a Price mismatch (claims 80 against 70) and TRX-2 an Unknown booking; both wait for Finance.
    private (CarrierInvoice Invoice, CarrierCost Cost) HaveReceivedInvoice()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        var invoice = CarrierInvoice.Receive(
            new CarrierInvoiceData(Carrier, "VDT-1", new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", 80m, null), new CarrierInvoiceLineData("TRX-2", 10m, null)], "invoice-1"),
            [cost],
            0m,
            Time.GetUtcNow()).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoice);
        _costs.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>()).Returns(cost);
        return (invoice, cost);
    }

    [Fact]
    public async Task Accept_ALineThatIsNotTheLast_InvoicesTheCostSavesAndPublishesNothing()
    {
        var (invoice, cost) = HaveReceivedInvoice();

        var result = await Accept.Handle(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Received");
        result.Value.Lines[0].Should().Match<CarrierInvoiceLineView>(line => line.Status == "Accepted" && line.AcceptedAmount == 80m);
        cost.Status.Should().Be(CarrierCostStatus.Invoiced);
        await _invoices.Received(1).UpdateAsync(invoice, Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Accept_TheLastLine_ApprovesAndPublishesOnceWithTheRemark()
    {
        var (invoice, _) = HaveReceivedInvoice();
        await Reject.Handle(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-2", "Unknown"), CancellationToken.None);
        _outbox.ClearReceivedCalls();

        var result = await Accept.Handle(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", 70m, "Agreed rate"), CancellationToken.None);

        result.Value.Status.Should().Be("Approved");
        result.Value.ApprovedAmount.Should().Be(70m);
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).Should().ContainSingle().Subject.Should().BeOfType<CarrierInvoiceApprovedV1>().Subject;
        published.CorrelationId.Should().Be("invoice-1");
        published.ApprovedAmount.Should().Be(70m);
        published.Lines.Should().Equal(
            new CarrierInvoiceApprovedLine("TRX-1", "Accepted", 80m, 70m, "Agreed rate"),
            new CarrierInvoiceApprovedLine("TRX-2", "Rejected", 10m, null, "Unknown"));
        invoice.Status.Should().Be(CarrierInvoiceStatus.Approved);
    }

    [Fact]
    public async Task Accept_AnAdjustedAmountWithoutARemark_IsRefusedAndSavesNothing()
    {
        var (_, cost) = HaveReceivedInvoice();

        var result = await Accept.Handle(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", 70m, null), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceAdjustedAmountNeedsRemark");
        cost.Status.Should().Be(CarrierCostStatus.Open);
        await _invoices.DidNotReceive().UpdateAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Accept_AnUnknownBooking_IsRefused_WithoutLookingForACost()
    {
        HaveReceivedInvoice();

        var result = await Accept.Handle(new AcceptCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-2", null, null), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceLineHasNoOpenCarrierCost");
        await _costs.DidNotReceive().FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decide_AnInvoiceTheErpDoesNotHave_IsRefused()
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns((CarrierInvoice?)null);

        var accepted = await Accept.Handle(new AcceptCarrierInvoiceLineCommand(Carrier, "NOPE", "TRX-1", null, null), CancellationToken.None);
        var rejected = await Reject.Handle(new RejectCarrierInvoiceLineCommand(Carrier, "NOPE", "TRX-1", "x"), CancellationToken.None);

        accepted.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceNotFound");
        rejected.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceNotFound");
    }

    [Fact]
    public async Task Reject_ALineThatIsNotTheLast_LeavesTheCostOpenAndPublishesNothing()
    {
        var (_, cost) = HaveReceivedInvoice();

        var result = await Reject.Handle(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "Not ours"), CancellationToken.None);

        result.Value.Lines[0].Should().Match<CarrierInvoiceLineView>(line => line.Status == "Rejected" && line.Remark == "Not ours" && line.AcceptedAmount == null);
        cost.Status.Should().Be(CarrierCostStatus.Open);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Reject_WithoutAReason_IsRefused()
    {
        HaveReceivedInvoice();

        var result = await Reject.Handle(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", null), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceRejectionNeedsReason");
        await _invoices.DidNotReceive().UpdateAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_TheLastLine_ApprovesAndPublishes()
    {
        HaveReceivedInvoice();
        await Reject.Handle(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-1", "no"), CancellationToken.None);

        var result = await Reject.Handle(new RejectCarrierInvoiceLineCommand(Carrier, "VDT-1", "TRX-2", "no"), CancellationToken.None);

        result.Value.Status.Should().Be("Approved");
        result.Value.ApprovedAmount.Should().Be(0m);
        _outbox.Received(1).Add(Arg.Any<CarrierInvoiceApprovedV1>());
    }
}
