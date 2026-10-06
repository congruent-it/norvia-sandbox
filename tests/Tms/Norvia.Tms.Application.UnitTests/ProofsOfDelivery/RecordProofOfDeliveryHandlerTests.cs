using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Record;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

public sealed class RecordProofOfDeliveryHandlerTests
{
    private static readonly byte[] CmrContent = [.. "%PDF-1.7"u8];

    private readonly IRepository<ProofOfDelivery> _proofsOfDelivery = Substitute.For<IRepository<ProofOfDelivery>>();
    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IExceptionNumbers _exceptionNumbers = Substitute.For<IExceptionNumbers>();
    private readonly IProofOfDeliveryFiles _files = Substitute.For<IProofOfDeliveryFiles>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(2));
    private readonly RecordProofOfDeliveryHandler _handler;

    public RecordProofOfDeliveryHandlerTests()
    {
        _exceptionNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ExceptionNumber.FromSequence(9));
        _handler = new RecordProofOfDeliveryHandler(_proofsOfDelivery, _carrierBookings, _shipments, _files, new ExceptionCauses(_exceptions, _exceptionNumbers), new ProofOfDeliveryRules(TimeSpan.FromMinutes(5)), _time);
    }

    [Fact]
    public async Task Handle_ForADeliveredShipment_RecordsItAsCurrent_KeepsTheFile_AndLeavesTheStatusAlone()
    {
        var shipment = Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShipmentNumber.Should().Be("SH-0000012");
        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Delivered);
        result.Value.DocumentId.Should().Be("doc-1");
        result.Value.NumberOfFiles.Should().Be(1);
        result.Value.CorrelationId.Should().Be("corr-pod");
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        var recorded = (ProofOfDelivery)_proofsOfDelivery.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ProofOfDelivery>.AddAsync)).GetArguments()[0]!;
        recorded.IsCurrent.Should().BeTrue();
        recorded.CorrelationId.Should().Be(new CorrelationId("corr-pod"));
        recorded.ReceivedAt.Should().Be(Samples.Now.AddDays(2));
        recorded.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProofOfDeliveryReceivedEvent>();
        await _files.Received(1).StoreAsync(shipment.Number, Cmr(), Arg.Is<ReadOnlyMemory<byte>>(content => content.ToArray().SequenceEqual(CmrContent)), Arg.Any<CancellationToken>());
        await _proofsOfDelivery.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ACleanProofOfDelivery_OpensNoException()
    {
        Found(ShipmentStatus.Delivered);

        await _handler.Handle(Command(), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AProofOfDeliveryWithReservations_OpensAReservationsException_WithItsCorrelationId_InTheSameSave()
    {
        var shipment = Found(ShipmentStatus.Delivered);

        await _handler.Handle(Command(Report(condition: DeliveryCondition.Reservations)), CancellationToken.None);

        var opened = OpenedException();
        opened.Type.Should().Be(ExceptionType.Reservations);
        opened.ShipmentId.Should().Be(shipment.Id);
        opened.CorrelationId.Should().Be(new CorrelationId("corr-pod"));
        opened.AfterDelivered.Should().BeFalse();
        opened.Causes.Should().ContainSingle().Which.SourceEventId.Should().Be("doc-1");
        opened.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>();
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _proofsOfDelivery.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnotherProofOfDeliveryWithReservationsWhileOneIsOpen_AddsACause_AndOpensNothingNew()
    {
        var shipment = Found(ShipmentStatus.Delivered);
        await _handler.Handle(Command(Report(condition: DeliveryCondition.Reservations)), CancellationToken.None);
        var open = OpenedException();
        open.ClearDomainEvents();
        _exceptions.ClearReceivedCalls();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.Reservations), Arg.Any<CancellationToken>())
            .Returns(open);

        await _handler.Handle(Command(Report(documentId: "doc-2", condition: DeliveryCondition.Reservations)), CancellationToken.None);

        open.Causes.Select(cause => cause.SourceEventId).Should().Equal("doc-1", "doc-2");
        open.DomainEvents.Should().BeEmpty();
        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ACleanProofOfDeliveryAfterOneWithReservations_MarksTheReservationsExceptionCauseCleared_WithoutPublishing()
    {
        var shipment = Found(ShipmentStatus.Delivered);
        await _handler.Handle(Command(Report(condition: DeliveryCondition.Reservations)), CancellationToken.None);
        var open = OpenedException();
        open.ClearDomainEvents();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.Reservations), Arg.Any<CancellationToken>())
            .Returns(open);

        await _handler.Handle(Command(Report(documentId: "doc-2", condition: DeliveryCondition.Clean)), CancellationToken.None);

        open.CauseCleared.Should().BeTrue();
        open.Status.Should().Be(ExceptionStatus.Open);
        open.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ACleanProofOfDeliveryWithNoOpenReservationsException_ChangesNoException()
    {
        Found(ShipmentStatus.Delivered);

        await _handler.Handle(Command(Report(condition: DeliveryCondition.Clean)), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
        await _proofsOfDelivery.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private ExceptionCase OpenedException() =>
        (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;

    [Theory]
    [InlineData(ShipmentStatus.Booked)]
    [InlineData(ShipmentStatus.Collected)]
    public async Task Handle_ForABookedOrLaterShipment_RecordsIt_WithoutChangingTheStatus(ShipmentStatus status)
    {
        var shipment = Found(status);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(status);
    }

    [Fact]
    public async Task Handle_ForAReferenceNoConfirmedBookingOfTheCarrierHas_IsRefusedWithShipmentNotFound_AndStoresNothing()
    {
        Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(Report(reference: "UNKNOWN")), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_FromOwnFleet_IsRefusedWithShipmentNotFound_ForNowOwnFleetFindsItsShipmentByTrip()
    {
        Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(Report(ProofOfDeliverySource.OwnFleet)), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_ForAShipmentThatIsNotBookedYet_IsRefusedWithShipmentNotInTransport()
    {
        Found(ShipmentStatus.Released);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.ShipmentNotInTransport);
    }

    [Fact]
    public async Task Handle_ARepeatOfTheCarriersDocumentId_ReturnsTheOriginalFlaggedAsARepeat_AndRecordsAndStoresNothing()
    {
        var shipment = Found(ShipmentStatus.Delivered);
        var original = ProofOfDelivery.Record(shipment, Report(), new CorrelationId("corr-original"), Samples.Now);
        _proofsOfDelivery.SingleOrDefaultAsync(Arg.Is<ProofOfDeliveryByDocumentIdSpec>(s => s.Source == Report().Source && s.DocumentId == "doc-1"), Arg.Any<CancellationToken>())
            .Returns(original);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsRepeat.Should().BeTrue();
        result.Value.ProofOfDeliveryId.Should().Be(original.Id.Value);
        result.Value.CorrelationId.Should().Be("corr-original");
        await _files.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default, TestContext.Current.CancellationToken);
        await _proofsOfDelivery.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
        await _proofsOfDelivery.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ANewDocumentIdForAShipmentWithACurrentOne_SupersedesIt_AndRecordsTheNewOneAsReplacing()
    {
        var shipment = Found(ShipmentStatus.Delivered);
        var earlier = ProofOfDelivery.Record(shipment, Report(documentId: "doc-0"), new CorrelationId("corr-old"), Samples.Now);
        _proofsOfDelivery.SingleOrDefaultAsync(Arg.Is<TrackedCurrentProofOfDeliverySpec>(s => s.ShipmentId == shipment.Id), Arg.Any<CancellationToken>()).Returns(earlier);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsRepeat.Should().BeFalse();
        earlier.IsCurrent.Should().BeFalse();
        var recorded = (ProofOfDelivery)_proofsOfDelivery.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ProofOfDelivery>.AddAsync)).GetArguments()[0]!;
        recorded.IsCurrent.Should().BeTrue();
        recorded.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProofOfDeliveryReceivedEvent>().Which.ReplacesEarlier.Should().BeTrue();
        await _proofsOfDelivery.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithOnlyTheCarrierBookingNumber_FindsTheShipmentByIt()
    {
        var shipment = Found(ShipmentStatus.Booked);
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByNumberSpec>(s => s.Number == BookingNumber && s.CarrierId == VanDijk), Arg.Any<CancellationToken>())
            .Returns(ConfirmedBooking(shipment));

        var result = await _handler.Handle(Command(Report(reference: "UNKNOWN") with { CarrierBookingNumber = BookingNumber }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ForADeliveryTimeBeyondTheClockSkew_IsRefused()
    {
        Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(Report() with { DeliveryTime = _time.GetUtcNow().AddMinutes(6) }), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.DeliveryTimeInTheFuture);
    }

    [Fact]
    public async Task Handle_ForADeliveryTimeWithinTheClockSkew_RecordsIt()
    {
        Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(Report() with { DeliveryTime = _time.GetUtcNow().AddMinutes(4) }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Handle_ForTooFewOrTooManyFiles_IsRefusedWithFileNotAccepted(int count)
    {
        Found(ShipmentStatus.Delivered);
        var command = new RecordProofOfDeliveryCommand(
            Report(files: [.. Enumerable.Repeat(Cmr(), count)]), [.. Enumerable.Repeat<ReadOnlyMemory<byte>>(CmrContent, count)], "corr-pod");

        var result = await _handler.Handle(command, CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.FileNotAccepted);
    }

    [Fact]
    public async Task Handle_ForAFileOver10MB_IsRefusedWithFileNotAccepted()
    {
        Found(ShipmentStatus.Delivered);
        var big = new byte[(10 * 1024 * 1024) + 1];
        CmrContent.CopyTo(big, 0);

        var result = await _handler.Handle(new RecordProofOfDeliveryCommand(Report(), [big], "corr-pod"), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.FileNotAccepted);
    }

    [Fact]
    public async Task Handle_StoresAFileAsWhatItsContentIs_NotAsWhatTheSenderDeclared()
    {
        Found(ShipmentStatus.Delivered);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];
        var file = new ProofOfDeliveryFile(Guid.NewGuid(), "photo.jpg", "image/jpeg", png.Length);

        var result = await _handler.Handle(new RecordProofOfDeliveryCommand(Report(files: [file]), [png], "corr-pod"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _files.Received(1).StoreAsync(Arg.Any<ShipmentNumber>(), Arg.Is<ProofOfDeliveryFile>(stored => stored.ContentType == "image/png"), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAHeicFileNamedJpg_IsRefusedWithFileNotAccepted()
    {
        Found(ShipmentStatus.Delivered);
        byte[] heic = [0, 0, 0, 0x18, .. "ftypheic"u8];
        var file = new ProofOfDeliveryFile(Guid.NewGuid(), "photo.jpg", "image/jpeg", heic.Length);

        var result = await _handler.Handle(new RecordProofOfDeliveryCommand(Report(files: [file]), [heic], "corr-pod"), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.FileNotAccepted);
    }

    [Fact]
    public async Task Handle_WithoutContentForEveryFile_IsABug()
    {
        Found(ShipmentStatus.Delivered);

        var act = async () => await _handler.Handle(Command() with { Files = [] }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_ForABookingWhoseShipmentIsGone_IsABug()
    {
        var shipment = ShipmentCarriedTo(ShipmentStatus.Delivered);
        _carrierBookings.SingleOrDefaultAsync(Arg.Any<ConfirmedCarrierBookingByReferenceSpec>(), Arg.Any<CancellationToken>()).Returns(ConfirmedBooking(shipment));

        var act = async () => await _handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task AssertRefusedAsync(Result<RecordProofOfDeliveryResult> result, ProofOfDeliveryRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _files.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default, default);
        await _proofsOfDelivery.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private Shipment Found(ShipmentStatus status)
    {
        var shipment = ShipmentCarriedTo(status);
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByReferenceSpec>(s => s.CarrierBookingReference == "VDT-1" && s.CarrierId == VanDijk), Arg.Any<CancellationToken>())
            .Returns(ConfirmedBooking(shipment));
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>()).Returns(shipment);
        return shipment;
    }

    private static RecordProofOfDeliveryCommand Command(ProofOfDeliveryReport? report = null) =>
        new(report ?? Report(), [CmrContent], "corr-pod");
}
