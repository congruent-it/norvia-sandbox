using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.ShipperInvoices.List;
using Norvia.Erp.Application.ShipperInvoices.Open;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.ShipperInvoices;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.ShipperInvoices;

public sealed class ListAndOpenShipperInvoicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private readonly IReadRepository<ShipperInvoice> _invoices = Substitute.For<IReadRepository<ShipperInvoice>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();

    private static ShipperInvoice Invoice(int sequence, string shipper = "SHP-0001")
    {
        var line = BillableLine.Record(
            new BillableLineData(
                "TO-000000" + sequence, new ShipperId(shipper), "PO", "SH-0000001", Now,
                new FreightCharge(10m, [new ChargeLine(ChargeLineType.Freight, 10m)], "RC-0001", 1), false, "intake", "billing"),
            Now).Line!;
        return ShipperInvoice.Issue(InvoiceNumber.Of(2026, sequence), new ShipperId(shipper), [line], Now, "corr-" + sequence);
    }

    private void KnowShippers() =>
        _shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns([new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true)]);

    [Fact]
    public async Task List_ShowsEachInvoiceWithItsShippersName_InTheOrderTheRepositoryGives()
    {
        _invoices.ListAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns([Invoice(2), Invoice(1, "SHP-0404")]);
        KnowShippers();

        var views = await new ListShipperInvoicesHandler(_invoices, _shippers).Handle(new ListShipperInvoicesQuery(), CancellationToken.None);

        views.Select(view => (view.InvoiceNumber, view.Shipper.Name)).Should().Equal(("INV-2026-000002", "Brouwer Dranken"), ("INV-2026-000001", "SHP-0404"));
        views[0].Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task Open_AnIssuedInvoice_ReturnsItWithItsLines()
    {
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>()).Returns(Invoice(1));
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));

        var view = await new OpenShipperInvoiceHandler(_invoices, _shippers).Handle(new OpenShipperInvoiceQuery("INV-2026-000001"), CancellationToken.None);

        view.Should().NotBeNull();
        view!.Shipper.Name.Should().Be("Brouwer Dranken");
        view.Lines.Single().TransportOrderNumber.Should().Be("TO-0000001");
        view.TotalWithVat.Should().Be(12.10m);
    }

    [Theory]
    [InlineData("INV-2026-999999")]
    [InlineData("")]
    public async Task Open_ANumberWithoutAnInvoice_ReturnsNull(string number)
    {
        var view = await new OpenShipperInvoiceHandler(_invoices, _shippers).Handle(new OpenShipperInvoiceQuery(number), CancellationToken.None);

        view.Should().BeNull();
    }
}

public sealed class ShipperInvoiceIssuedV1Tests
{
    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task Serialize_AsTheOutboxDoes_HasTheFieldsOfTheCanonicalModelSchema()
    {
        var now = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        var line = BillableLine.Record(
            new BillableLineData(
                "TO-0000001", new ShipperId("SHP-0001"), "PO", "SH-0000001", now,
                new FreightCharge(10m, [new ChargeLine(ChargeLineType.Freight, 10m)], "RC-0001", 1), false, "intake", "billing"),
            now).Line!;
        var invoices = Substitute.For<IReadRepository<ShipperInvoice>>();
        invoices.ListAsync(Arg.Any<ISpecification<ShipperInvoice>>(), Arg.Any<CancellationToken>())
            .Returns([ShipperInvoice.Issue(InvoiceNumber.Of(2026, 1), new ShipperId("SHP-0001"), [line], now, "corr-1")]);
        var shippers = Substitute.For<IReadRepository<Shipper>>();
        shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns([]);
        var view = (await new ListShipperInvoicesHandler(invoices, shippers)
            .Handle(new ListShipperInvoicesQuery(), CancellationToken.None))[0];
        var issued = new Norvia.Erp.Application.ShipperInvoices.ShipperInvoiceIssuedV1(Guid.CreateVersion7(), now, "corr-1", view);

        using var body = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(issued, issued.GetType(), Web));

        var root = body.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("eventId", "occurredAt", "correlationId", "invoice", "eventType", "eventVersion");
        root.GetProperty("eventType").GetString().Should().Be("ShipperInvoiceIssued");
        var document = root.GetProperty("invoice");
        document.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "invoiceNumber", "invoiceDate", "shipper", "lines", "currency", "totalWithoutVat", "vat", "totalWithVat", "issuedAt", "correlationId", "status");
        document.GetProperty("invoiceDate").GetString().Should().Be("2026-10-19");
        document.GetProperty("lines")[0].EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "transportOrderNumber", "shipperReference", "shipmentNumber", "deliveryDate", "chargeLines", "total");
    }
}
