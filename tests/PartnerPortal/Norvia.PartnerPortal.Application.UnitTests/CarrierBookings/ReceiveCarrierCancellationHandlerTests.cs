using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierBookings.Cancel;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class ReceiveCarrierCancellationHandlerTests
{
    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop(null, null, "Rotterdam", "NLD", null, null), new CarrierBookingStop(null, null, "Düsseldorf", "DEU", null, null), null, []);

    private readonly IRepository<CarrierBookingRequest> _repository = Substitute.For<IRepository<CarrierBookingRequest>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly ReceiveCarrierCancellationHandler _handler;

    public ReceiveCarrierCancellationHandlerTests()
    {
        _personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        _handler = new ReceiveCarrierCancellationHandler(_repository, _personas, new FakeTimeProvider(Samples.Now));
    }

    private static CarrierBookingRequest Request(bool confirmed)
    {
        var request = CarrierBookingRequest.Receive(Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now.AddDays(-1), Details);
        if (confirmed)
        {
            request.Answer(new CarrierAnswer.Confirmation("VDT-77812"), "answer-1", Samples.Now.AddHours(-20));
        }

        return request;
    }

    private void InboxHolds(CarrierBookingRequest? request) =>
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierBookingRequestToAnswerSpec>(), Arg.Any<CancellationToken>()).Returns(request);

    private static ReceiveCarrierCancellationCommand Cancel(string partyId = "CAR-0001", string? reason = "Truck broke down") =>
        new(partyId, "CB-0000001", CarrierCancellationKind.Cancelled, reason, "cancel-1");

    private static ReceiveCarrierCancellationCommand Withdraw() =>
        new("CAR-0001", "CB-0000001", CarrierCancellationKind.Withdrawn, null, "withdraw-1");

    [Fact]
    public async Task Handle_ACancellationOfAConfirmedBooking_ShowsItCancelledByNorvia_WithTheReason()
    {
        var request = Request(confirmed: true);
        InboxHolds(request);

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierCancellationResult("CB-0000001", Applied: true, Repeated: false));
        request.Status.Should().Be(CarrierBookingRequestStatus.CancelledByNorvia);
        request.CancellationText.Should().Be("Truck broke down");
        request.CancelledAt.Should().Be(Samples.Now);
        await _repository.Received(1).UpdateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameCancellationAgain_IsAcknowledged_AndChangesNothing()
    {
        var request = Request(confirmed: true);
        request.CancelledByNorvia("Truck broke down", Samples.Now.AddMinutes(-5));
        InboxHolds(request);

        var result = await _handler.Handle(Cancel(reason: "Truck broke down"), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierCancellationResult("CB-0000001", Applied: false, Repeated: true));
        request.CancelledAt.Should().Be(Samples.Now.AddMinutes(-5));
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_AWithdrawalOfAnOpenRequest_ShowsItWithdrawnByNorvia()
    {
        var request = Request(confirmed: false);
        InboxHolds(request);

        var result = await _handler.Handle(Withdraw(), CancellationToken.None);

        result.Value.Applied.Should().BeTrue();
        request.Status.Should().Be(CarrierBookingRequestStatus.WithdrawnByNorvia);
        request.ClosedAt.Should().Be(Samples.Now);
        await _repository.Received(1).UpdateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameWithdrawalAgain_IsAcknowledged_AndChangesNothing()
    {
        var request = Request(confirmed: false);
        request.WithdrawnByNorvia("withdraw-1", Samples.Now.AddMinutes(-5));
        InboxHolds(request);

        var result = await _handler.Handle(Withdraw(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierCancellationResult("CB-0000001", Applied: false, Repeated: true));
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_AWithdrawalOfARequestTheCarrierConfirmed_LeavesItConfirmed_AndAcknowledges()
    {
        var request = Request(confirmed: true);
        InboxHolds(request);

        var result = await _handler.Handle(Withdraw(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierCancellationResult("CB-0000001", Applied: false, Repeated: false));
        request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ACancellationOfABookingTheInboxNeverHad_IsAcknowledged()
    {
        InboxHolds(null);

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReceiveCarrierCancellationResult("CB-0000001", Applied: false, Repeated: false));
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, CancellationToken.None);
    }

    [Theory]
    [InlineData("CAR-9999")]
    [InlineData("SHP-0001")]
    public async Task Handle_ForNoCarrierPersona_IsNotFound(string partyId)
    {
        var result = await _handler.Handle(Cancel(partyId), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_ACancellationWithoutAReason_IsInvalid()
    {
        InboxHolds(Request(confirmed: true));

        var result = await _handler.Handle(Cancel(reason: " "), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, CancellationToken.None);
    }
}
