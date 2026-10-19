using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.BillableLines.Record;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.BillableLines;

public sealed class RecordBillableLineHandlerTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    private static readonly BillableLineData Data = new(
        "TO-0000123",
        new ShipperId("SHP-0001"),
        "PO-77",
        "SH-0000123",
        new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(2)),
        new FreightCharge(100m, [new ChargeLine(ChargeLineType.Freight, 80m), new ChargeLine(ChargeLineType.MinimumChargeAdjustment, 20m)], "RC-0001", 1),
        ReleasedWithoutProofOfDelivery: true,
        "intake-1",
        "billing-1");

    private readonly IRepository<BillableLine> _lines = Substitute.For<IRepository<BillableLine>>();
    private readonly IReadRepository<Shipper> _shippers = Substitute.For<IReadRepository<Shipper>>();

    private RecordBillableLineHandler Handler => new(_lines, _shippers, Time);

    private void KnowShipper() =>
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>())
            .Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));

    private void HaveRecorded(BillableLineData data) =>
        _lines.FirstOrDefaultAsync(Arg.Any<ISpecification<BillableLine>>(), Arg.Any<CancellationToken>())
            .Returns(BillableLine.Record(data, Time.GetUtcNow().AddHours(-1)).Line);

    [Fact]
    public async Task Handle_ANewTransportOrder_RecordsItAsWaiting()
    {
        KnowShipper();

        var result = await Handler.Handle(new RecordBillableLineCommand(Data), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeFalse();
        var line = result.Value.Line;
        line.TransportOrderNumber.Should().Be("TO-0000123");
        line.Status.Should().Be("Waiting");
        line.ShipperId.Should().Be("SHP-0001");
        line.FreightCharge.Total.Should().Be(100m);
        line.FreightCharge.Currency.Should().Be("EUR");
        line.FreightCharge.ChargeLines.Select(chargeLine => chargeLine.Type).Should().Equal("Freight", "MinimumChargeAdjustment");
        line.ReleasedWithoutProofOfDelivery.Should().BeTrue();
        line.IntakeCorrelationId.Should().Be("intake-1");
        line.BillingCorrelationId.Should().Be("billing-1");
        line.RecordedAt.Should().Be(Time.GetUtcNow());
        await _lines.Received(1).AddAsync(Arg.Is<BillableLine>(added => added.Id.TransportOrderNumber == "TO-0000123"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameContentAgain_ReturnsTheOriginalLineAsARepeat()
    {
        HaveRecorded(Data);

        var result = await Handler.Handle(new RecordBillableLineCommand(Data with { BillingCorrelationId = "billing-2" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeTrue();
        result.Value.Line.BillingCorrelationId.Should().Be("billing-1", "it is the original line");
        await _lines.DidNotReceive().AddAsync(Arg.Any<BillableLine>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DifferentContentForARecordedTransportOrder_IsRefusedAsAConflict()
    {
        HaveRecorded(Data);

        var result = await Handler.Handle(new RecordBillableLineCommand(Data with { ShipperReference = "PO-99" }), CancellationToken.None);

        AssertRefusedWith(result, BillableLineRefusalCode.BillableLineConflict);
        await _lines.DidNotReceive().AddAsync(Arg.Any<BillableLine>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownShipper_IsRefused()
    {
        var result = await Handler.Handle(new RecordBillableLineCommand(Data), CancellationToken.None);

        AssertRefusedWith(result, BillableLineRefusalCode.ShipperNotFound);
        result.ValidationErrors.Single().ErrorMessage.Should().Contain("SHP-0001");
        await _lines.DidNotReceive().AddAsync(Arg.Any<BillableLine>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AFreightChargeWithoutChargeLines_IsRefused()
    {
        KnowShipper();

        var result = await Handler.Handle(new RecordBillableLineCommand(Data with { FreightCharge = Data.FreightCharge with { Total = 0, ChargeLines = [] } }), CancellationToken.None);

        AssertRefusedWith(result, BillableLineRefusalCode.FreightChargeWithoutChargeLines);
    }

    [Fact]
    public async Task Handle_AFreightChargeWhoseTotalIsNotTheSumOfItsLines_IsRefused()
    {
        KnowShipper();

        var result = await Handler.Handle(new RecordBillableLineCommand(Data with { FreightCharge = Data.FreightCharge with { Total = 99m } }), CancellationToken.None);

        AssertRefusedWith(result, BillableLineRefusalCode.FreightChargeTotalMismatch);
        await _lines.DidNotReceive().AddAsync(Arg.Any<BillableLine>(), Arg.Any<CancellationToken>());
    }

    private static void AssertRefusedWith(Result<RecordBillableLineResult> result, BillableLineRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be(code.ToString());
        result.ValidationErrors.Single().ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TrackBillableLine_ARecording_ContinuesTheTrailOfTheTransactionThatMadeItBillable()
    {
        KnowShipper();
        var logger = new CapturingLogger<TrackBillableLineInLoggingScope>();

        await new TrackBillableLineInLoggingScope(logger).Handle(
            new RecordBillableLineCommand(Data),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("correlationId") && (string)scope["correlationId"]! == "billing-1"
            && (string)scope["transportOrderNumber"]! == "TO-0000123" && (string)scope["shipmentNumber"]! == "SH-0000123");
        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Succeeded");
    }

    [Fact]
    public async Task TrackBillableLine_ARefusal_RecordsRefusedWithItsFailureCode()
    {
        var logger = new CapturingLogger<TrackBillableLineInLoggingScope>();

        await new TrackBillableLineInLoggingScope(logger).Handle(
            new RecordBillableLineCommand(Data),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Refused"
            && (string)scope["failureCode"]! == "ShipperNotFound");
    }

    [Fact]
    public async Task TrackBillableLine_ARepeat_RecordsSucceeded()
    {
        HaveRecorded(Data);
        var logger = new CapturingLogger<TrackBillableLineInLoggingScope>();

        await new TrackBillableLineInLoggingScope(logger).Handle(
            new RecordBillableLineCommand(Data),
            (message, token) => Handler.Handle(message, token),
            CancellationToken.None);

        logger.Scopes.Should().Contain(scope => scope.ContainsKey("outcome") && (string)scope["outcome"]! == "Succeeded");
    }
}

/// <summary>Remembers the keys of every logging scope begun on it.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<Dictionary<string, object?>> Scopes { get; } = [];

    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> keys)
        {
            Scopes.Add(keys.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        return NullLogger.Instance.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}
