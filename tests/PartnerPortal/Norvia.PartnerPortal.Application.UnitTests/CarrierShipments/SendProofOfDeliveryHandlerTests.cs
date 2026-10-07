using Norvia.PartnerPortal.Domain.Personas;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

public sealed class SendProofOfDeliveryHandlerTests
{
    private static readonly ProofOfDeliveryUpload Cmr = new("cmr.pdf", "application/pdf", "%PDF-1.4"u8.ToArray());

    private readonly IReadRepository<CarrierBookingRequest> _repository = Substitute.For<IReadRepository<CarrierBookingRequest>>();
    private readonly RecordingPartnerApi _partnerApi = new();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly SendProofOfDeliveryHandler _handler;

    public SendProofOfDeliveryHandlerTests()
    {
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        GivenShipment(ConfirmedRequest());
        _handler = new SendProofOfDeliveryHandler(_repository, _partnerApi, _personas, _time);
    }

    private static CarrierBookingRequest ConfirmedRequest(CarrierBookingStop? delivery = null, bool confirm = true)
    {
        var request = CarrierBookingRequest.Receive(
            Samples.VanDijkTransport.Id, "CB-0000001", "SH-0000042", "release-1", Samples.Now.AddHours(-1),
            new CarrierBookingDetails(
                new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
                delivery ?? new CarrierBookingStop("Oudegracht 112", "3511 AW", "Utrecht", "NLD", null, null), null, []));
        if (confirm)
        {
            request.Answer(new CarrierAnswer.Confirmation("VDT-77812"), "answer-1", Samples.Now.AddMinutes(-30));
        }

        return request;
    }

    private void GivenShipment(CarrierBookingRequest request) =>
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns(request);

    private ValueTask<Result<SentProofOfDelivery>> Send(
        string receivedBy = "J. Jansen",
        ProofOfDeliveryCondition condition = ProofOfDeliveryCondition.Clean,
        string? remark = null,
        IReadOnlyList<ProofOfDeliveryUpload>? files = null,
        string persona = "van-dijk-transport") =>
        _handler.Handle(new SendProofOfDeliveryCommand(new PersonaId(persona), "CB-0000001", receivedBy, condition, remark, files ?? [Cmr]), CancellationToken.None);

    [Fact]
    public async Task Handle_SendsTheProofToNorviaAsTheCarrierPersona_WithItsBookingReferenceNumberAndFiles()
    {
        var result = await Send(receivedBy: " J. Jansen ");

        result.IsSuccess.Should().BeTrue();
        var (persona, shipmentNumber, proof) = _partnerApi.ProofsOfDelivery.Should().ContainSingle().Subject;
        persona.Should().Be(Samples.VanDijkTransport);
        shipmentNumber.Should().Be("SH-0000042");
        proof.BookingReference.Should().Be("VDT-77812");
        proof.CarrierBookingNumber.Should().Be("CB-0000001");
        proof.ReceivedBy.Should().Be("J. Jansen");
        proof.Condition.Should().Be(ProofOfDeliveryCondition.Clean);
        proof.Remark.Should().BeNull();
        proof.DeliveryTime.Should().Be(Samples.Now);
        proof.Files.Should().ContainSingle().Which.Should().BeEquivalentTo(new CarrierProofOfDeliveryFile("cmr.pdf", "application/pdf", Cmr.Content));
        result.Value.Reply.Should().Be(_partnerApi.Reply);
    }

    [Fact]
    public async Task Handle_EachProofHasAUniqueDocumentId()
    {
        await Send();
        await Send();

        _partnerApi.ProofsOfDelivery.Select(sent => sent.Proof.DocumentId).Should().OnlyHaveUniqueItems().And.NotContainNulls();
    }

    [Fact]
    public async Task Handle_TheLocationIsTheShipmentsDeliveryAddress()
    {
        await Send();

        var proof = _partnerApi.ProofsOfDelivery.Should().ContainSingle().Subject.Proof;
        (proof.Place, proof.Postcode, proof.Country).Should().Be(("Utrecht", "3511 AW", "NLD"));
    }

    [Fact]
    public async Task Handle_WithReservations_SendsTheRemark()
    {
        await Send(condition: ProofOfDeliveryCondition.Reservations, remark: " Two pallets damaged ");

        var proof = _partnerApi.ProofsOfDelivery.Should().ContainSingle().Subject.Proof;
        (proof.Condition, proof.Remark).Should().Be((ProofOfDeliveryCondition.Reservations, "Two pallets damaged"));
    }

    [Fact]
    public async Task Handle_CleanWithARemarkLeftOver_SendsNone()
    {
        await Send(remark: "Left over from reservations");

        _partnerApi.ProofsOfDelivery.Should().ContainSingle().Which.Proof.Remark.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NorviaRefusesIt_StillReturnsTheReply_ForThePortalToTell()
    {
        _partnerApi.Reply = TrackingReply.Refused("Not a Carrier.", "corr-2");

        var result = await Send();

        result.Value.Reply.Result.Should().Be(TrackingResult.Refused);
    }

    [Fact]
    public async Task Handle_KeepsTheShipmentAsItWas_ForAProofNeverChangesIt()
    {
        var request = ConfirmedRequest();
        GivenShipment(request);

        var result = await Send();

        result.Value.Shipment.Should().BeEquivalentTo(CarrierShipmentView.From(request));
        request.Stage.Should().Be(ShipmentStage.Booked);
    }

    [Fact]
    public async Task Handle_ForAShipmentAlreadyDelivered_StillSendsIt()
    {
        var request = ConfirmedRequest();
        request.ReportTracking(TrackingEventType.Delivered);
        GivenShipment(request);

        (await Send()).IsSuccess.Should().BeTrue();
        _partnerApi.ProofsOfDelivery.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Handle_WithoutReceivedBy_IsInvalid_AndSendsNothing(string receivedBy)
    {
        var result = await Send(receivedBy: receivedBy);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("name of the person");
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Handle_WithReservationsAndNoRemark_IsInvalid_AndSendsNothing(string? remark)
    {
        var result = await Send(condition: ProofOfDeliveryCondition.Reservations, remark: remark);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("needs a remark");
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CleanWithoutARemark_IsFine() => (await Send(remark: null)).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Handle_WithTooFewOrTooManyFiles_IsInvalid_AndSendsNothing(int count)
    {
        var result = await Send(files: [.. Enumerable.Repeat(Cmr, count)]);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("1 to 5 files");
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FiveFiles_AreSent() =>
        (await Send(files: [.. Enumerable.Repeat(Cmr, 5)])).Value.ProofOfDelivery.Files.Should().HaveCount(5);

    [Theory]
    [InlineData(0)]
    [InlineData(SendProofOfDeliveryHandler.MaxFileBytes + 1)]
    public async Task Handle_AnEmptyOrOversizedFile_IsInvalid_AndSendsNothing(int size)
    {
        var result = await Send(files: [Cmr, new ProofOfDeliveryUpload("big.pdf", "application/pdf", new byte[size])]);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("big.pdf");
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AFileOfExactlyTheLimit_IsSent() =>
        (await Send(files: [new ProofOfDeliveryUpload("big.pdf", "application/pdf", new byte[SendProofOfDeliveryHandler.MaxFileBytes])])).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Handle_WhenTheDeliveryAddressHasNoPlace_IsInvalid_AndSendsNothing()
    {
        GivenShipment(ConfirmedRequest(new CarrierBookingStop("Oudegracht 112", "3511 AW", null, "NLD", null, null)));

        var result = await Send();

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("delivery address");
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForAPersonaThatIsNoCarrier_IsNotFound_AndSendsNothing()
    {
        var result = await Send(persona: "brouwer-dranken");

        result.Status.Should().Be(ResultStatus.NotFound);
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForANobody_IsNotFound() => (await Send(persona: "nobody")).Status.Should().Be(ResultStatus.NotFound);

    [Fact]
    public async Task Handle_ForAShipmentTheCarrierHasNot_IsNotFound()
    {
        _repository.FirstOrDefaultAsync(Arg.Is<CarrierBookingRequestToAnswerSpec>(spec => spec != null), Arg.Any<CancellationToken>()).Returns((CarrierBookingRequest?)null);

        (await Send()).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_ForABookingNotYetConfirmed_IsNotFound_AndSendsNothing()
    {
        GivenShipment(ConfirmedRequest(confirm: false));

        (await Send()).Status.Should().Be(ResultStatus.NotFound);
        _partnerApi.ProofsOfDelivery.Should().BeEmpty();
    }
}
