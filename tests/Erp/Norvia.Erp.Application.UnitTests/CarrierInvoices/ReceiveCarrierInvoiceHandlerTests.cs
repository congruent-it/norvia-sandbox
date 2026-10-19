using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Receive;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.UnitTests.BillableLines;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.CarrierInvoices;

public sealed class ReceiveCarrierInvoiceHandlerTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private static readonly CarrierId Carrier = new("CAR-0001");

    private readonly IRepository<CarrierInvoice> _invoices = Substitute.For<IRepository<CarrierInvoice>>();
    private readonly IRepository<CarrierCost> _costs = Substitute.For<IRepository<CarrierCost>>();
    private readonly IReadRepository<Carrier> _carriers = Substitute.For<IReadRepository<Carrier>>();
    private readonly IMatchingTolerance _tolerance = Substitute.For<IMatchingTolerance>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private ReceiveCarrierInvoiceHandler Handler => new(_invoices, _costs, _carriers, _tolerance, _outbox, Time);

    private static CarrierInvoiceData Data(params CarrierInvoiceLineData[] lines) =>
        new(Carrier, "VDT-1", new DateOnly(2026, 10, 19), lines.Length == 0 ? [new CarrierInvoiceLineData("TRX-1", 70m, "Utrecht")] : lines, "invoice-1");

    private static CarrierCost Cost(string number, string reference, decimal total) =>
        CarrierCost.Record(
            new CarrierCostData(
                number, reference, Carrier, "SH-0000001", "TO-0000001",
                new CarrierCostCharge(total, [new ChargeLine(ChargeLineType.Freight, total)]), CarrierCostSource.FromSpot("remark"), Time.GetUtcNow(), "delivered"),
            Time.GetUtcNow()).Cost!;

    private void KnowCarrier() =>
        _carriers.FirstOrDefaultAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>())
            .Returns(new Carrier(Carrier, "Van Dijk Transport", true));

    private void HaveCosts(params CarrierCost[] costs) =>
        _costs.ListAsync(Arg.Any<ISpecification<CarrierCost>>(), Arg.Any<CancellationToken>()).Returns(costs.ToList());

    [Fact]
    public async Task Handle_AllLinesMatch_ApprovesAtOncePublishesOnceAndInvoicesTheCost()
    {
        KnowCarrier();
        var cost = Cost("CB-0000001", "TRX-1", 70m);
        HaveCosts(cost);

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeFalse();
        var invoice = result.Value.Invoice;
        invoice.Status.Should().Be("Approved");
        invoice.ApprovedAmount.Should().Be(70m);
        invoice.CorrelationId.Should().Be("invoice-1");
        invoice.Lines.Single().Should().Match<CarrierInvoiceLineView>(line => line.Result == "Matched" && line.Status == "Matched" && line.CarrierBookingNumber == "CB-0000001");
        cost.Status.Should().Be(CarrierCostStatus.Invoiced);
        await _invoices.Received(1).AddAsync(Arg.Is<CarrierInvoice>(added => added.InvoiceNumber == "VDT-1"), Arg.Any<CancellationToken>());
        var published = _outbox.ReceivedCalls().Select(call => call.GetArguments()[0]).Should().ContainSingle().Subject.Should().BeOfType<CarrierInvoiceApprovedV1>().Subject;
        published.CarrierId.Should().Be("CAR-0001");
        published.InvoiceNumber.Should().Be("VDT-1");
        published.ApprovedAmount.Should().Be(70m);
        published.CorrelationId.Should().Be("invoice-1");
        published.OccurredAt.Should().Be(Time.GetUtcNow());
        published.Lines.Should().Equal(new CarrierInvoiceApprovedLine("TRX-1", "Matched", 70m, 70m, null));
        published.Topic.Should().Be(Topics.InvoicingEvents);
        published.EventType.Should().Be("CarrierInvoiceApproved");
        published.ApplicationProperties.Should().Contain(new KeyValuePair<string, object>(TrackingKeys.Flow, "carrier-invoices"))
            .And.Contain(new KeyValuePair<string, object>(TrackingKeys.PartyId, "CAR-0001"))
            .And.Contain(new KeyValuePair<string, object>(TrackingKeys.DocumentNumber, "VDT-1"))
            .And.Contain(new KeyValuePair<string, object>(TrackingKeys.CorrelationId, "invoice-1"));
    }

    [Fact]
    public async Task Handle_ALineToDecide_IsReceivedAndPublishesNothing()
    {
        KnowCarrier();
        HaveCosts(Cost("CB-0000001", "TRX-1", 60m));

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data()), CancellationToken.None);

        result.Value.Invoice.Status.Should().Be("Received");
        result.Value.Invoice.ApprovedAmount.Should().BeNull();
        result.Value.Invoice.Lines.Single().Result.Should().Be("PriceMismatch");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _invoices.Received(1).AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheToleranceSetting_DecidesWhatMatches()
    {
        KnowCarrier();
        HaveCosts(Cost("CB-0000001", "TRX-1", 69.50m));
        _tolerance.Amount.Returns(0.50m);

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data()), CancellationToken.None);

        result.Value.Invoice.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task Handle_ARefWithNoCostFromThisCarrier_IsAnUnknownBookingNotAFailure()
    {
        KnowCarrier();
        HaveCosts();

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoice.Lines.Single().Result.Should().Be("UnknownBooking");
    }

    [Fact]
    public async Task Handle_LooksUpOnlyTheCostsOfTheCarrierWithTheDistinctReferences()
    {
        KnowCarrier();
        HaveCosts();

        await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data(new CarrierInvoiceLineData("TRX-1", 5m, null), new CarrierInvoiceLineData("TRX-2", 5m, null))), CancellationToken.None);

        var spec = _costs.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<CarrierCostsOfCarrierByReferencesSpec>().Should().ContainSingle().Subject;
        spec.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_TheSameContentAgain_ReturnsTheOriginalAsARepeatAndPublishesNothing()
    {
        var original = CarrierInvoice.Receive(Data(), [Cost("CB-0000001", "TRX-1", 70m)], 0m, Time.GetUtcNow().AddHours(-1)).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(original);

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data() with { CorrelationId = "invoice-2" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeTrue();
        result.Value.Invoice.CorrelationId.Should().Be("invoice-1", "it is the original Carrier invoice");
        result.Value.Invoice.ReceivedAt.Should().Be(Time.GetUtcNow().AddHours(-1));
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _invoices.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DifferentContentUnderAUsedNumber_IsRefusedAsInvoiceNumberUsed()
    {
        var original = CarrierInvoice.Receive(Data(), [], 0m, Time.GetUtcNow().AddHours(-1)).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(original);

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data(new CarrierInvoiceLineData("TRX-1", 71m, null))), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        var error = result.ValidationErrors.Single();
        error.ErrorCode.Should().Be("InvoiceNumberUsed");
        error.ErrorMessage.Should().Contain("CAR-0001").And.Contain("VDT-1");
        await _invoices.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownCarrier_IsRefusedAsCarrierNotFound()
    {
        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data()), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierNotFound");
        await _invoices.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_ABreachOfAReceivingRule_IsRefusedWithItsCodeAndStoresAndPublishesNothing()
    {
        KnowCarrier();
        HaveCosts();

        var result = await Handler.Handle(new ReceiveCarrierInvoiceCommand(Data(new CarrierInvoiceLineData("TRX-1", 0m, null))), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("CarrierInvoiceLineAmountNotAboveZero");
        await _invoices.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task TrackCarrierInvoice_AReceipt_ContinuesTheTrailApiManagementStarted()
    {
        KnowCarrier();
        HaveCosts(Cost("CB-0000001", "TRX-1", 70m));
        var logger = new CapturingLogger<TrackCarrierInvoiceInLoggingScope>();

        await new TrackCarrierInvoiceInLoggingScope(logger).Handle(
            new ReceiveCarrierInvoiceCommand(Data()),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("correlationId") && (string)scope["correlationId"]! == "invoice-1"
            && (string)scope["documentNumber"]! == "VDT-1" && (string)scope["partyId"]! == "CAR-0001");
        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("received as Approved");
    }

    [Fact]
    public async Task TrackCarrierInvoice_ARefusal_RecordsRefusedWithItsFailureCode()
    {
        var logger = new CapturingLogger<TrackCarrierInvoiceInLoggingScope>();

        await new TrackCarrierInvoiceInLoggingScope(logger).Handle(
            new ReceiveCarrierInvoiceCommand(Data()),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Refused"
            && (string)scope["failureCode"]! == "CarrierNotFound");
    }

    [Fact]
    public async Task TrackCarrierInvoice_ARepeat_RecordsSucceeded()
    {
        var original = CarrierInvoice.Receive(Data(), [], 0m, Time.GetUtcNow().AddHours(-1)).Invoice!;
        _invoices.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierInvoice>>(), Arg.Any<CancellationToken>()).Returns(original);
        var logger = new CapturingLogger<TrackCarrierInvoiceInLoggingScope>();

        await new TrackCarrierInvoiceInLoggingScope(logger).Handle(
            new ReceiveCarrierInvoiceCommand(Data()),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Succeeded");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("already received");
    }
}
