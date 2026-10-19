using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.CarrierInvoices.List;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.CarrierInvoices;

public sealed class ListCarrierInvoicesToDecideHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CarrierId Carrier = new("CAR-0001");
    private readonly IReadRepository<CarrierInvoice> _invoices = Substitute.For<IReadRepository<CarrierInvoice>>();
    private readonly IReadRepository<CarrierCost> _costs = Substitute.For<IReadRepository<CarrierCost>>();
    private readonly IReadRepository<Carrier> _carriers = Substitute.For<IReadRepository<Carrier>>();

    private ListCarrierInvoicesToDecideHandler Handler => new(_invoices, _costs, _carriers);

    private static CarrierCost Cost(string number, string reference, decimal total) =>
        CarrierCost.Record(
            new CarrierCostData(
                number, reference, Carrier, "SH-0000001", "TO-0000001",
                new CarrierCostCharge(total, [new ChargeLine(ChargeLineType.Freight, total)]), CarrierCostSource.FromSpot("remark"), Now, "delivered"),
            Now).Cost!;

    [Fact]
    public async Task List_ShowsEachInvoiceWithTheCostsOfItsLines_AndTheCarriersName()
    {
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        var invoice = CarrierInvoice.Receive(
            new CarrierInvoiceData(Carrier, "VDT-1", new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", 80m, null), new CarrierInvoiceLineData("TRX-2", 5m, null)], "invoice"),
            [cost],
            0m,
            Now).Invoice!;
        _invoices.ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns([invoice]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns([new Carrier(Carrier, "Van Dijk Transport", true)]);
        _costs.ListAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>()).Returns([cost]);

        var items = await Handler.Handle(new ListCarrierInvoicesToDecideQuery(), CancellationToken.None);

        var item = items.Should().ContainSingle().Subject;
        item.CarrierName.Should().Be("Van Dijk Transport");
        item.Invoice.InvoiceNumber.Should().Be("VDT-1");
        item.Costs.Keys.Should().Equal("CB-0000001");
        item.Costs["CB-0000001"].Total.Should().Be(70m);
    }

    [Fact]
    public async Task List_WhereNoLineHasACost_ReadsNoCosts()
    {
        var invoice = CarrierInvoice.Receive(
            new CarrierInvoiceData(Carrier, "VDT-1", new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-2", 5m, null)], "invoice"), [], 0m, Now).Invoice!;
        _invoices.ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns([invoice]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns([]);

        var items = await Handler.Handle(new ListCarrierInvoicesToDecideQuery(), CancellationToken.None);

        items.Single().CarrierName.Should().Be("CAR-0001");
        items.Single().Costs.Should().BeEmpty();
        await _costs.DidNotReceive().ListAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_WithNone_IsEmpty()
    {
        _invoices.ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns([]);

        (await Handler.Handle(new ListCarrierInvoicesToDecideQuery(), CancellationToken.None)).Should().BeEmpty();
    }
}
