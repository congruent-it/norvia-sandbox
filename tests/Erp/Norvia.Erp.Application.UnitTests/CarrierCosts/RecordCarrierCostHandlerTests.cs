using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CarrierCosts.Record;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.UnitTests.BillableLines;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.CarrierCosts;

public sealed class RecordCarrierCostHandlerTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    private static readonly CarrierCostData Data = new(
        "CB-0000123",
        "TRX-998",
        new CarrierId("CAR-0001"),
        "SH-0000123",
        "TO-0000123",
        new CarrierCostCharge(70m, [new ChargeLine(ChargeLineType.Freight, 60m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 10m)]),
        CarrierCostSource.FromSpot("Ferry strike, no Carrier Rate Card lane"),
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        "delivered-1");

    private readonly IRepository<CarrierCost> _costs = Substitute.For<IRepository<CarrierCost>>();
    private readonly IReadRepository<Carrier> _carriers = Substitute.For<IReadRepository<Carrier>>();

    private readonly IRepository<CarrierInvoice> _invoices = Substitute.For<IRepository<CarrierInvoice>>();
    private readonly IMatchingTolerance _tolerance = Substitute.For<IMatchingTolerance>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public RecordCarrierCostHandlerTests() => WaitingInvoices();

    private RecordCarrierCostHandler Handler => new(_costs, _invoices, _carriers, _tolerance, _outbox, Time);

    private void WaitingInvoices(params CarrierInvoice[] invoices) =>
        _invoices.ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(invoices.ToList());

    private static CarrierInvoice Received(string number, decimal amount, params string[] otherReferences) =>
        CarrierInvoice.Receive(
            new CarrierInvoiceData(
                new CarrierId("CAR-0001"), number, new DateOnly(2026, 10, 19),
                [new CarrierInvoiceLineData("TRX-998", amount, null), .. otherReferences.Select(reference => new CarrierInvoiceLineData(reference, 5m, null))],
                "invoice-" + number),
            [], 0m, Time.GetUtcNow()).Invoice!;

    private void KnowCarrier() =>
        _carriers.FirstOrDefaultAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>())
            .Returns(new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true));

    private void HaveRecorded(CarrierCostData data) =>
        _costs.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>())
            .Returns(CarrierCost.Record(data, Time.GetUtcNow().AddHours(-1)).Cost);

    [Fact]
    public async Task Handle_ANewCarrierBooking_RecordsItAsOpen()
    {
        KnowCarrier();

        var result = await Handler.Handle(new RecordCarrierCostCommand(Data), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeFalse();
        var cost = result.Value.Cost;
        cost.CarrierBookingNumber.Should().Be("CB-0000123");
        cost.CarrierBookingReference.Should().Be("TRX-998");
        cost.Status.Should().Be("Open");
        cost.CarrierId.Should().Be("CAR-0001");
        cost.Currency.Should().Be("EUR");
        cost.Total.Should().Be(70m);
        cost.ChargeLines.Select(line => line.Type).Should().Equal("Freight", "MinimumChargeAdjustment");
        cost.Source.Should().Be(new Norvia.Erp.Application.CarrierCosts.CarrierCostSourceView("Spot", null, null, "Ferry strike, no Carrier Rate Card lane"));
        cost.CorrelationId.Should().Be("delivered-1");
        cost.RecordedAt.Should().Be(Time.GetUtcNow());
        await _costs.Received(1).AddAsync(Arg.Is<CarrierCost>(added => added.Id.CarrierBookingNumber == "CB-0000123"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AWaitingUnknownBookingLine_IsMatchedAgainAndTheInvoiceApprovedOnce()
    {
        KnowCarrier();
        var invoice = Received("VDT-1", 70m);
        WaitingInvoices(invoice);

        var result = await Handler.Handle(new RecordCarrierCostCommand(Data), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoice.Status.Should().Be(CarrierInvoiceStatus.Approved);
        invoice.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.Matched);
        _outbox.Received(1).Add(Arg.Is<CarrierInvoiceApprovedV1>(approved => approved.InvoiceNumber == "VDT-1"));
        await _costs.Received(1).AddAsync(Arg.Any<CarrierCost>(), Arg.Any<CancellationToken>());
        await _invoices.DidNotReceive().UpdateAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AWaitingLineThatIsNotTheLastToDecide_PublishesNothing()
    {
        KnowCarrier();
        var invoice = Received("VDT-1", 99m, "TRX-other");
        WaitingInvoices(invoice);

        await Handler.Handle(new RecordCarrierCostCommand(Data), CancellationToken.None);

        invoice.Lines[0].Result.Should().Be(CarrierInvoiceLineResult.PriceMismatch);
        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
        _outbox.DidNotReceive().Add(Arg.Any<CarrierInvoiceApprovedV1>());
    }

    [Fact]
    public async Task Handle_TwoInvoicesWaitingForOneReference_TheOldestGetsTheCostAndTheNextIsADuplicate()
    {
        KnowCarrier();
        var first = Received("VDT-1", 70m);
        var second = Received("VDT-2", 70m);
        WaitingInvoices(first, second);

        await Handler.Handle(new RecordCarrierCostCommand(Data), CancellationToken.None);

        first.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.Matched);
        second.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.Duplicate);
        _outbox.Received(1).Add(Arg.Any<CarrierInvoiceApprovedV1>());
    }

    [Fact]
    public async Task Handle_TheSameContentAgain_ReturnsTheOriginalAsARepeat()
    {
        HaveRecorded(Data);

        var result = await Handler.Handle(new RecordCarrierCostCommand(Data with { CorrelationId = "delivered-2" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeTrue();
        result.Value.Cost.CorrelationId.Should().Be("delivered-1", "it is the original Carrier cost");
        await _costs.DidNotReceive().AddAsync(Arg.Any<CarrierCost>(), Arg.Any<CancellationToken>());
        await _invoices.DidNotReceive().ListAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<CarrierInvoiceApprovedV1>());
    }

    [Fact]
    public async Task Handle_DifferentContentForARecordedBooking_IsRefusedAsAConflict()
    {
        HaveRecorded(Data);

        var result = await Handler.Handle(new RecordCarrierCostCommand(Data with { CarrierBookingReference = "TRX-999" }), CancellationToken.None);

        AssertRefusedWith(result, CarrierCostRefusalCode.CarrierCostConflict);
        await _costs.DidNotReceive().AddAsync(Arg.Any<CarrierCost>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownCarrier_IsRefused()
    {
        var result = await Handler.Handle(new RecordCarrierCostCommand(Data), CancellationToken.None);

        AssertRefusedWith(result, CarrierCostRefusalCode.CarrierNotFound);
        result.ValidationErrors.Single().ErrorMessage.Should().Contain("CAR-0001");
        await _costs.DidNotReceive().AddAsync(Arg.Any<CarrierCost>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ACostWithoutChargeLines_IsRefused()
    {
        KnowCarrier();

        var result = await Handler.Handle(new RecordCarrierCostCommand(Data with { Charge = new CarrierCostCharge(0, []) }), CancellationToken.None);

        AssertRefusedWith(result, CarrierCostRefusalCode.CarrierCostWithoutChargeLines);
    }

    [Fact]
    public async Task Handle_ATotalThatIsNotTheSumOfItsLines_IsRefused()
    {
        KnowCarrier();

        var result = await Handler.Handle(new RecordCarrierCostCommand(Data with { Charge = Data.Charge with { Total = 99m } }), CancellationToken.None);

        AssertRefusedWith(result, CarrierCostRefusalCode.CarrierCostTotalMismatch);
        await _costs.DidNotReceive().AddAsync(Arg.Any<CarrierCost>(), Arg.Any<CancellationToken>());
    }

    private static void AssertRefusedWith(Result<RecordCarrierCostResult> result, CarrierCostRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be(code.ToString());
        result.ValidationErrors.Single().ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TrackCarrierCost_ARecording_ContinuesTheTrailOfTheDeliveredTrackingEvent()
    {
        KnowCarrier();
        var logger = new CapturingLogger<TrackCarrierCostInLoggingScope>();

        await new TrackCarrierCostInLoggingScope(logger).Handle(
            new RecordCarrierCostCommand(Data),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("correlationId") && (string)scope["correlationId"]! == "delivered-1"
            && (string)scope["carrierBookingNumber"]! == "CB-0000123" && (string)scope["shipmentNumber"]! == "SH-0000123"
            && (string)scope["transportOrderNumber"]! == "TO-0000123" && (string)scope["partyId"]! == "CAR-0001");
        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("recorded as Open");
    }

    [Fact]
    public async Task TrackCarrierCost_ARefusal_RecordsRefusedWithItsFailureCode()
    {
        var logger = new CapturingLogger<TrackCarrierCostInLoggingScope>();

        await new TrackCarrierCostInLoggingScope(logger).Handle(
            new RecordCarrierCostCommand(Data),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Refused"
            && (string)scope["failureCode"]! == "CarrierNotFound");
    }

    [Fact]
    public async Task TrackCarrierCost_ARepeat_RecordsSucceeded()
    {
        HaveRecorded(Data);
        var logger = new CapturingLogger<TrackCarrierCostInLoggingScope>();

        await new TrackCarrierCostInLoggingScope(logger).Handle(
            new RecordCarrierCostCommand(Data),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("already recorded");
    }
}
