using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Draft;
using Norvia.PartnerPortal.Application.Bookings.List;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.List;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.Personas.List;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;
using Norvia.PartnerPortal.Application.ShipperTracking.FindTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.GetTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.List;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class PartnerPortalRequestsTests : IDisposable
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ServiceProvider _services;
    private readonly PartnerPortalRequests _portal;

    public PartnerPortalRequestsTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _mediator).BuildServiceProvider();
        _portal = new PartnerPortalRequests(_services.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task ListPersonasAsync_ListsThePersonas()
    {
        _mediator.Send(Arg.Any<ListPersonasQuery>(), Arg.Any<CancellationToken>()).Returns(Samples.Personas);

        (await _portal.ListPersonasAsync(Xunit.TestContext.Current.CancellationToken)).Should().Equal(Samples.Personas);
    }

    [Fact]
    public async Task DraftBookingAsync_ForAPersona_IsItsDraft_AndNullForNone()
    {
        var draft = Samples.Draft();
        _mediator.Send(new DraftBookingQuery(new PersonaId("brouwer-dranken")), Arg.Any<CancellationToken>()).Returns(Result.Success(draft));
        _mediator.Send(new DraftBookingQuery(new PersonaId("nobody")), Arg.Any<CancellationToken>()).Returns(Result<BookingRequest>.NotFound());

        (await _portal.DraftBookingAsync(new PersonaId("brouwer-dranken"), Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(draft);
        (await _portal.DraftBookingAsync(new PersonaId("nobody"), Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SubmitBookingAsync_SendsItAsThePersona_AndReturnsNorviasAnswer()
    {
        var booking = Samples.Draft();
        var answered = Samples.Answered(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        _mediator.Send(new SubmitBookingCommand(new PersonaId("brouwer-dranken"), booking), Arg.Any<CancellationToken>()).Returns(Result.Success(answered));

        (await _portal.SubmitBookingAsync(new PersonaId("brouwer-dranken"), booking, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answered);
    }

    [Fact]
    public async Task ListTrackedTransportOrdersAsync_AsksForTheFilteredList_AsThePersona()
    {
        var filter = new ShipperTrackingFilter(ShipperTrackingStatus.Collected, Page: 2);
        var answer = TrackingAnswer.Found<IReadOnlyList<ShipperTrackingSummary>>([]);
        _mediator.Send(new ListShipperTrackingQuery(new PersonaId("brouwer-dranken"), filter), Arg.Any<CancellationToken>()).Returns(answer);

        (await _portal.ListTrackedTransportOrdersAsync(new PersonaId("brouwer-dranken"), filter, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answer);
    }

    [Fact]
    public async Task GetTrackedTransportOrderAsync_AsksForItByNumber_AsThePersona()
    {
        var answer = TrackingAnswer.NotFound<ShipperTrackingDetail>("No.");
        _mediator.Send(new GetShipperTrackingQuery(new PersonaId("brouwer-dranken"), "TO-0000042"), Arg.Any<CancellationToken>()).Returns(answer);

        (await _portal.GetTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "TO-0000042", Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answer);
    }

    [Fact]
    public async Task FindTrackedTransportOrderAsync_AsksForItByTheShipperReference_AsThePersona()
    {
        var answer = TrackingAnswer.NotFound<ShipperTrackingDetail>("No.");
        _mediator.Send(new FindShipperTrackingQuery(new PersonaId("brouwer-dranken"), "BD-1"), Arg.Any<CancellationToken>()).Returns(answer);

        (await _portal.FindTrackedTransportOrderAsync(new PersonaId("brouwer-dranken"), "BD-1", Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answer);
    }

    [Fact]
    public async Task DownloadProofOfDeliveryFileAsync_AsksForTheFile_AsThePersona()
    {
        var fileId = Guid.CreateVersion7();
        var answer = TrackingAnswer.Found(new ShipperProofFileContent("cmr.pdf", "application/pdf", [1]));
        _mediator.Send(new DownloadProofOfDeliveryFileQuery(new PersonaId("brouwer-dranken"), "TO-0000042", fileId), Arg.Any<CancellationToken>()).Returns(answer);

        (await _portal.DownloadProofOfDeliveryFileAsync(new PersonaId("brouwer-dranken"), "TO-0000042", fileId, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answer);
    }

    [Fact]
    public async Task AnswerCarrierBookingAsync_SendsTheAnswerAsThePersona_AndReturnsWhatBecameOfIt_OrNullForNone()
    {
        var answer = new CarrierAnswer.Confirmation("VDT-1");
        var details = new CarrierBookingDetails(new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, []);
        var answered = new AnsweredCarrierBooking(new CarrierBookingRequestView(Guid.CreateVersion7(), "CB-0000001", "SH-0000001", Samples.SentAt, details), NorviaReply.Recorded("m-1"));
        _mediator.Send(new AnswerCarrierBookingCommand(new PersonaId("van-dijk-transport"), "CB-0000001", answer), Arg.Any<CancellationToken>()).Returns(Result.Success(answered));
        _mediator.Send(new AnswerCarrierBookingCommand(new PersonaId("van-dijk-transport"), "CB-0000009", answer), Arg.Any<CancellationToken>()).Returns(Result<AnsweredCarrierBooking>.NotFound());

        (await _portal.AnswerCarrierBookingAsync(new PersonaId("van-dijk-transport"), "CB-0000001", answer, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answered);
        (await _portal.AnswerCarrierBookingAsync(new PersonaId("van-dijk-transport"), "CB-0000009", answer, Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ListCarrierShipmentsAsync_ListsThePersonasShipments()
    {
        IReadOnlyList<CarrierShipmentView> shipments = [];
        _mediator.Send(new ListCarrierShipmentsQuery(new PersonaId("van-dijk-transport")), Arg.Any<CancellationToken>()).Returns(shipments);

        (await _portal.ListCarrierShipmentsAsync(new PersonaId("van-dijk-transport"), Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(shipments);
    }

    [Fact]
    public async Task SendTrackingEventAsync_SendsTheEventAsThePersona_AndReturnsWhatBecameOfIt_OrNullForNone()
    {
        var details = new CarrierBookingDetails(new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, []);
        var shipment = new CarrierShipmentView("CB-0000001", "SH-0000001", "VDT-1", details, ShipmentStage.Collected, [], []);
        var sent = new SentTrackingEvent(
            shipment, new CarrierTrackingEvent("evt-1", "VDT-1", "CB-0000001", TrackingEventType.Collected, Samples.SentAt, "Rotterdam", "3087 BM", "NLD"), TrackingReply.Accepted("m-1"));
        _mediator.Send(new SendTrackingCommand(new PersonaId("van-dijk-transport"), "CB-0000001", TrackingEventType.Collected), Arg.Any<CancellationToken>()).Returns(Result.Success(sent));
        _mediator.Send(new SendTrackingCommand(new PersonaId("van-dijk-transport"), "CB-0000009", TrackingEventType.Collected), Arg.Any<CancellationToken>()).Returns(Result<SentTrackingEvent>.NotFound());

        (await _portal.SendTrackingEventAsync(new PersonaId("van-dijk-transport"), "CB-0000001", TrackingEventType.Collected, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(sent);
        (await _portal.SendTrackingEventAsync(new PersonaId("van-dijk-transport"), "CB-0000009", TrackingEventType.Collected, Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SendProofOfDeliveryAsync_SendsTheDraftAsThePersona_AndReturnsWhatBecameOfIt_OrNullForNone()
    {
        var details = new CarrierBookingDetails(new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, []);
        var shipment = new CarrierShipmentView("CB-0000001", "SH-0000001", "VDT-1", details, ShipmentStage.Delivered, [], []);
        var files = new[] { new ProofOfDeliveryUpload("cmr.pdf", "application/pdf", [1]) };
        var sent = new SentProofOfDelivery(
            shipment, new CarrierProofOfDelivery("doc-1", "VDT-1", "CB-0000001", "J. Jansen", Samples.SentAt, "Utrecht", "3511 AW", "NLD", ProofOfDeliveryCondition.Clean, null, []),
            TrackingReply.Accepted("m-1"));
        var draft = new ProofOfDeliveryDraft("J. Jansen", ProofOfDeliveryCondition.Clean, null, files);
        _mediator.Send(new SendProofOfDeliveryCommand(new PersonaId("van-dijk-transport"), "CB-0000001", "J. Jansen", ProofOfDeliveryCondition.Clean, null, files), Arg.Any<CancellationToken>())
            .Returns(Result.Success(sent));
        _mediator.Send(new SendProofOfDeliveryCommand(new PersonaId("van-dijk-transport"), "CB-0000009", "J. Jansen", ProofOfDeliveryCondition.Clean, null, files), Arg.Any<CancellationToken>())
            .Returns(Result<SentProofOfDelivery>.NotFound());

        (await _portal.SendProofOfDeliveryAsync(new PersonaId("van-dijk-transport"), "CB-0000001", draft, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(sent);
        (await _portal.SendProofOfDeliveryAsync(new PersonaId("van-dijk-transport"), "CB-0000009", draft, Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ListBookingsAsync_ListsThePersonasBookings()
    {
        IReadOnlyList<BookingView> bookings = [Samples.Answered(NorviaAnswer.NotReceived(null))];
        _mediator.Send(new ListBookingsQuery(new PersonaId("brouwer-dranken")), Arg.Any<CancellationToken>()).Returns(bookings);

        (await _portal.ListBookingsAsync(new PersonaId("brouwer-dranken"), Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(bookings);
    }

    [Fact]
    public async Task ARequestThatFails_Throws_RatherThanSayingThereIsNone()
    {
        _mediator.Send(new DraftBookingQuery(new PersonaId("brouwer-dranken")), Arg.Any<CancellationToken>()).Returns(Result<BookingRequest>.Error("The seed file is gone."));

        var draft = () => _portal.DraftBookingAsync(new PersonaId("brouwer-dranken"), Xunit.TestContext.Current.CancellationToken);

        await draft.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetCarrierAnswerSettingsAsync_ReadsTheSettings_OrIsNullWithoutThePersona()
    {
        var settings = new CarrierAnswerSettingsView(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, 5);
        _mediator.Send(new GetCarrierAnswerSettingsQuery(new PersonaId("kessler-spedition")), Arg.Any<CancellationToken>()).Returns(Result.Success(settings));
        _mediator.Send(new GetCarrierAnswerSettingsQuery(new PersonaId("nobody")), Arg.Any<CancellationToken>()).Returns(Result<CarrierAnswerSettingsView>.NotFound());

        (await _portal.GetCarrierAnswerSettingsAsync(new PersonaId("kessler-spedition"), Xunit.TestContext.Current.CancellationToken)).Should().Be(settings);
        (await _portal.GetCarrierAnswerSettingsAsync(new PersonaId("nobody"), Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ChangeCarrierAnswerSettingsAsync_ChangesTheSettings_OrIsNullWithoutThePersona()
    {
        var settings = new CarrierAnswerSettingsView(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, 3);
        _mediator.Send(new ChangeCarrierAnswerSettingsCommand(new PersonaId("kessler-spedition"), CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, 3), Arg.Any<CancellationToken>())
            .Returns(Result.Success(settings));
        _mediator.Send(new ChangeCarrierAnswerSettingsCommand(new PersonaId("nobody"), CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, 3), Arg.Any<CancellationToken>())
            .Returns(Result<CarrierAnswerSettingsView>.NotFound());

        (await _portal.ChangeCarrierAnswerSettingsAsync(new PersonaId("kessler-spedition"), settings, Xunit.TestContext.Current.CancellationToken)).Should().Be(settings);
        (await _portal.ChangeCarrierAnswerSettingsAsync(new PersonaId("nobody"), settings, Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task GetCarrierTrackingSettingsAsync_ReadsTheSettings_OrIsNullWithoutThePersona()
    {
        var settings = new CarrierTrackingSettingsView(TrackingMode.AutoProgress, 5);
        _mediator.Send(new GetCarrierTrackingSettingsQuery(new PersonaId("kessler-spedition")), Arg.Any<CancellationToken>()).Returns(Result.Success(settings));
        _mediator.Send(new GetCarrierTrackingSettingsQuery(new PersonaId("nobody")), Arg.Any<CancellationToken>()).Returns(Result<CarrierTrackingSettingsView>.NotFound());

        (await _portal.GetCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), Xunit.TestContext.Current.CancellationToken)).Should().Be(settings);
        (await _portal.GetCarrierTrackingSettingsAsync(new PersonaId("nobody"), Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ChangeCarrierTrackingSettingsAsync_ChangesTheSettings_OrIsNullWithoutThePersona()
    {
        var settings = new CarrierTrackingSettingsView(TrackingMode.AutoProgress, 3);
        _mediator.Send(new ChangeCarrierTrackingSettingsCommand(new PersonaId("kessler-spedition"), TrackingMode.AutoProgress, 3), Arg.Any<CancellationToken>()).Returns(Result.Success(settings));
        _mediator.Send(new ChangeCarrierTrackingSettingsCommand(new PersonaId("nobody"), TrackingMode.AutoProgress, 3), Arg.Any<CancellationToken>())
            .Returns(Result<CarrierTrackingSettingsView>.NotFound());

        (await _portal.ChangeCarrierTrackingSettingsAsync(new PersonaId("kessler-spedition"), settings, Xunit.TestContext.Current.CancellationToken)).Should().Be(settings);
        (await _portal.ChangeCarrierTrackingSettingsAsync(new PersonaId("nobody"), settings, Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
