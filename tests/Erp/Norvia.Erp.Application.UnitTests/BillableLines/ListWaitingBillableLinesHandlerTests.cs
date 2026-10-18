using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Erp.Application.BillableLines.ListWaiting;
using Norvia.Erp.Application.BillableLines.ListWaitingOfShipper;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.BillableLines;

public sealed class ListWaitingBillableLinesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly IReadRepository<BillableLine> _lines = Substitute.For<IReadRepository<BillableLine>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();

    private static BillableLine Line(string number, string shipperId, decimal amount, bool withoutProof = false) =>
        BillableLine.Record(
            new BillableLineData(
                number,
                new ShipperId(shipperId),
                "PO-" + number,
                "SH-0000001",
                Now,
                new FreightCharge(amount, [new ChargeLine(ChargeLineType.Freight, amount)], "RC-0001", 1),
                withoutProof,
                "intake",
                "billing"),
            Now).Line!;

    [Fact]
    public async Task ListWaiting_CountsAndTotalsEachShippersLines_ByShipperName()
    {
        _lines.ListAsync(Arg.Any<ISpecification<BillableLine>>(), Arg.Any<CancellationToken>())
            .Returns([Line("TO-0000001", "SHP-0002", 10.25m), Line("TO-0000002", "SHP-0001", 5m), Line("TO-0000003", "SHP-0002", 20m)]);
        _shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns([new Shipper(new ShipperId("SHP-0001"), "Zeeland Zaden", true), new Shipper(new ShipperId("SHP-0002"), "Brouwer Dranken", true)]);

        var views = await new ListWaitingBillableLinesHandler(_lines, _shippers).Handle(new ListWaitingBillableLinesQuery(), CancellationToken.None);

        views.Should().Equal(new WaitingShipperView("SHP-0002", "Brouwer Dranken", 2, 30.25m), new WaitingShipperView("SHP-0001", "Zeeland Zaden", 1, 5m));
    }

    [Fact]
    public async Task ListWaiting_WithNoLines_IsEmpty()
    {
        _lines.ListAsync(Arg.Any<ISpecification<BillableLine>>(), Arg.Any<CancellationToken>()).Returns([]);
        _shippers.ListAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns([]);

        var views = await new ListWaitingBillableLinesHandler(_lines, _shippers).Handle(new ListWaitingBillableLinesQuery(), CancellationToken.None);

        views.Should().BeEmpty();
    }

    [Fact]
    public async Task ListWaitingOfShipper_ShowsEachLineWithEverythingItHolds()
    {
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _lines.ListAsync(Arg.Any<ISpecification<BillableLine>>(), Arg.Any<CancellationToken>())
            .Returns([Line("TO-0000001", "SHP-0001", 10m, withoutProof: true), Line("TO-0000002", "SHP-0001", 5.5m)]);

        var view = await new ListWaitingBillableLinesOfShipperHandler(_lines, _shippers)
            .Handle(new ListWaitingBillableLinesOfShipperQuery("SHP-0001"), CancellationToken.None);

        view.Should().NotBeNull();
        view!.ShipperName.Should().Be("Brouwer Dranken");
        view.Total.Should().Be(15.5m);
        view.Lines.Select(line => line.TransportOrderNumber).Should().Equal("TO-0000001", "TO-0000002");
        view.Lines[0].ReleasedWithoutProofOfDelivery.Should().BeTrue();
        view.Lines[0].FreightCharge.ChargeLines.Should().ContainSingle();
    }

    [Fact]
    public async Task ListWaitingOfShipper_AnUnknownShipper_IsNull()
    {
        var view = await new ListWaitingBillableLinesOfShipperHandler(_lines, _shippers)
            .Handle(new ListWaitingBillableLinesOfShipperQuery("SHP-9999"), CancellationToken.None);

        view.Should().BeNull();
    }
}
