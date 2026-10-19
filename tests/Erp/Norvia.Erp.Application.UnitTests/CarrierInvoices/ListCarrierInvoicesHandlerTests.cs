using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.CarrierInvoices.List;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.CarrierInvoices;

public sealed class ListCarrierInvoicesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private readonly IReadRepository<CarrierInvoice> _invoices = Substitute.For<IReadRepository<CarrierInvoice>>();
    private readonly IReadRepository<Carrier> _carriers = Substitute.For<IReadRepository<Carrier>>();

    private static CarrierInvoice Invoice(string carrierId, string number, decimal amount) =>
        CarrierInvoice.Receive(
            new CarrierInvoiceData(new CarrierId(carrierId), number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", amount, null)], "invoice"),
            [],
            0m,
            Now).Invoice!;

    [Fact]
    public async Task List_ShowsEachInvoiceWithItsCarriersNameStatusAndTotal_InTheOrderOfTheSpecification()
    {
        _invoices.ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>())
            .Returns([Invoice("CAR-0002", "B-1", 20m), Invoice("CAR-0001", "A-1", 10.25m), Invoice("CAR-0009", "C-1", 5m)]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>())
            .Returns([new Carrier(new CarrierId("CAR-0001"), "Zeeland Vervoer", true), new Carrier(new CarrierId("CAR-0002"), "Van Dijk Transport", true)]);

        var items = await new ListCarrierInvoicesHandler(_invoices, _carriers).Handle(new ListCarrierInvoicesQuery(), CancellationToken.None);

        items.Select(item => (item.CarrierName, item.Invoice.InvoiceNumber, item.Invoice.Status, item.Invoice.Total))
            .Should().Equal(("Van Dijk Transport", "B-1", "Received", 20m), ("Zeeland Vervoer", "A-1", "Received", 10.25m), ("CAR-0009", "C-1", "Received", 5m));
    }

    [Fact]
    public async Task List_WithNone_IsEmpty()
    {
        _invoices.ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns([]);
        _carriers.ListAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns([]);

        var items = await new ListCarrierInvoicesHandler(_invoices, _carriers).Handle(new ListCarrierInvoicesQuery(), CancellationToken.None);

        items.Should().BeEmpty();
    }
}
