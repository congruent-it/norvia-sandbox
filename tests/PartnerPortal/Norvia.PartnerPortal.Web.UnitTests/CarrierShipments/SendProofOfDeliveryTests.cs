using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierShipments;

/// <summary>A Carrier Persona sends a Proof of delivery by hand: the form, what Norvia replied, and how the list shows it.</summary>
public sealed class SendProofOfDeliveryTests : PortalWebContext
{
    private static readonly CarrierBookingDetails Details = new(
        new CarrierBookingStop("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", null, null),
        new CarrierBookingStop("Oudegracht 112", "3511 AW", "Utrecht", "NLD", null, null),
        null,
        []);

    private static CarrierShipmentView Shipment(string number = "CB-0000001", TrackingEventType? lastProgress = null) => new(
        number, "SH-0000042", "VDT-77812", Details, TrackingRules.StageOf(lastProgress),
        TrackingRules.NextProgress(lastProgress), TrackingRules.Exceptions(lastProgress));

    private IRenderedComponent<Shipments> RenderShipments(params CarrierShipmentView[] shipments)
    {
        Portal.ListCarrierShipmentsAsync(new PersonaId("van-dijk-transport"), Arg.Any<CancellationToken>()).Returns(shipments);
        return Render<Shipments>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));
    }

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<Shipments> page, string number = "CB-0000001") =>
        page.Find($"section[aria-label=\"Send Proof of delivery for {number}\"]");

    private void NorviaReplies(TrackingReply reply, string number = "CB-0000001") =>
        Portal.SendProofOfDeliveryAsync(new PersonaId("van-dijk-transport"), number, Arg.Any<ProofOfDeliveryDraft>(), Arg.Any<CancellationToken>())
            .Returns(new SentProofOfDelivery(
                Shipment(number),
                new CarrierProofOfDelivery("doc-1", "VDT-77812", number, "J. Jansen", DateTimeOffset.UnixEpoch, "Utrecht", "3511 AW", "NLD", ProofOfDeliveryCondition.Clean, null, []),
                reply));

    private IRenderedComponent<Shipments> OpenForm(string number = "CB-0000001", params CarrierShipmentView[] shipments)
    {
        var page = RenderShipments(shipments.Length == 0 ? [Shipment(number)] : shipments);
        page.Find($"[aria-label=\"Send Proof of delivery for {number}\"]").Click();
        return page;
    }

    private static void FillIn(IRenderedComponent<Shipments> page, string receivedBy = "J. Jansen") =>
        Field(Panel(page), "Received by").Change(receivedBy);

    private void NothingWasSent() =>
        Portal.DidNotReceive().SendProofOfDeliveryAsync(Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<ProofOfDeliveryDraft>(), Arg.Any<CancellationToken>());

    private static ProofOfDeliveryDraft Draft(IEnumerable<NSubstitute.Core.ICall> calls) =>
        (ProofOfDeliveryDraft)calls.Single(call => call.GetMethodInfo().Name == nameof(IPartnerPortal.SendProofOfDeliveryAsync)).GetArguments()[2]!;

    [Fact]
    public void Render_EachShipment_OffersSendProofOfDelivery_Booked_InTransit_AndDelivered()
    {
        var page = RenderShipments(Shipment("CB-0000001"), Shipment("CB-0000002", TrackingEventType.Departed), Shipment("CB-0000003", TrackingEventType.Delivered));

        page.FindAll("[aria-label^=\"Send Proof of delivery for \"]").Select(button => button.GetAttribute("aria-label"))
            .Should().Equal("Send Proof of delivery for CB-0000001", "Send Proof of delivery for CB-0000002", "Send Proof of delivery for CB-0000003");
    }

    [Fact]
    public void Render_SendsNothingByItself()
    {
        var page = OpenForm();
        Time.Advance(Shipments.RefreshInterval);

        page.WaitForAssertion(() => Panel(page).Should().NotBeNull());
        NothingWasSent();
    }

    [Fact]
    public void Open_ShowsTheFormForThatShipment_WithTheDeliveryAddressAndTheSampleCmrAttached()
    {
        var page = OpenForm("CB-0000002", Shipment("CB-0000001"), Shipment("CB-0000002"));

        var panel = Panel(page, "CB-0000002");
        Field(panel, "Received by").Should().NotBeNull();
        Field(panel, "Remark").Should().NotBeNull();
        panel.QuerySelector("fluent-select")!.Should().NotBeNull();
        panel.TextContent.Should().Contain("Utrecht, 3511 AW, NLD").And.Contain("sample-cmr.pdf");
        Field(panel, "Attach the sample CMR (sample-cmr.pdf)").HasAttribute("checked").Should().BeTrue();
        page.FindAll("section[aria-label^=\"Send Proof of delivery for \"]").Should().ContainSingle();
    }

    [Fact]
    public void Send_WithoutReceivedBy_SaysSoNextToTheField_AndSendsNothing()
    {
        var page = OpenForm();

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).TextContent.Should().Contain(ProofOfDeliveryForm.ReceivedByRequired));
        NothingWasSent();
    }

    [Fact]
    public void Send_WithReservationsAndNoRemark_AsksForOne_AndSendsNothing()
    {
        var page = OpenForm();
        FillIn(page);
        Panel(page).QuerySelector("fluent-select")!.Change("Reservations");

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).TextContent.Should().Contain(ProofOfDeliveryForm.RemarkRequired));
        NothingWasSent();
    }

    [Fact]
    public void Send_WithNoFileAtAll_SaysSo_AndSendsNothing()
    {
        var page = OpenForm();
        FillIn(page);
        Field(Panel(page), "Attach the sample CMR (sample-cmr.pdf)").Change(false);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).QuerySelector("[role=alert]")!.TextContent.Should().Contain("Attach 1 to 5 files"));
        NothingWasSent();
    }

    [Fact]
    public void Send_Clean_SendsWhoReceivedItAndTheSampleCmr_AsThePersona_ForThatShipment()
    {
        NorviaReplies(TrackingReply.Accepted("corr-1"));
        var page = OpenForm();
        FillIn(page, " J. Jansen ");

        Panel(page).QuerySelector("form")!.Submit();

        var draft = Draft(Portal.ReceivedCalls());
        (draft.ReceivedBy, draft.Condition, draft.Remark).Should().Be(("J. Jansen", ProofOfDeliveryCondition.Clean, null));
        var file = draft.Files.Should().ContainSingle().Subject;
        (file.Name, file.ContentType).Should().Be(("sample-cmr.pdf", "application/pdf"));
        file.Content.Take(5).Should().Equal("%PDF-"u8.ToArray());
        Portal.Received(1).SendProofOfDeliveryAsync(new PersonaId("van-dijk-transport"), "CB-0000001", Arg.Any<ProofOfDeliveryDraft>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Send_WithReservations_SendsTheRemark()
    {
        NorviaReplies(TrackingReply.Accepted("corr-1"));
        var page = OpenForm();
        FillIn(page);
        Panel(page).QuerySelector("fluent-select")!.Change("Reservations");
        Field(Panel(page), "Remark").Change("Two pallets damaged");

        Panel(page).QuerySelector("form")!.Submit();

        var draft = Draft(Portal.ReceivedCalls());
        (draft.Condition, draft.Remark).Should().Be((ProofOfDeliveryCondition.Reservations, "Two pallets damaged"));
    }

    [Fact]
    public void Send_UploadedFiles_GoWithTheSampleCmr_Or_Instead()
    {
        NorviaReplies(TrackingReply.Accepted("corr-1"));
        var page = OpenForm();
        FillIn(page);
        Field(Panel(page), "Attach the sample CMR (sample-cmr.pdf)").Change(false);
        page.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([0x89, 0x50, 0x4E, 0x47], "signature.png", contentType: "image/png"),
            InputFileContent.CreateFromBinary("%PDF-1.4"u8.ToArray(), "scan.pdf", contentType: "application/pdf"));

        Panel(page).QuerySelector("form")!.Submit();

        var files = Draft(Portal.ReceivedCalls()).Files;
        files.Select(file => (file.Name, file.ContentType)).Should().Equal(("signature.png", "image/png"), ("scan.pdf", "application/pdf"));
        files[0].Content.Should().Equal(0x89, 0x50, 0x4E, 0x47);
    }

    [Fact]
    public void Send_MoreThanFiveFiles_SaysSo_AndSendsNothing()
    {
        var page = OpenForm();
        FillIn(page);
        page.FindComponent<InputFile>().UploadFiles([.. Enumerable.Range(1, 5).Select(i => InputFileContent.CreateFromBinary([1], $"photo{i}.png", contentType: "image/png"))]);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).QuerySelector("[role=alert]")!.TextContent.Should().Contain("not 6"));
        NothingWasSent();
    }

    [Fact]
    public void Send_AFileOver10MB_SaysSo_AndSendsNothing()
    {
        var page = OpenForm();
        FillIn(page);
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[SendProofOfDeliveryHandler.MaxFileBytes + 1], "huge.pdf", contentType: "application/pdf"));

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).QuerySelector("[role=alert]")!.TextContent.Should().Contain("huge.pdf is over 10 MB"));
        NothingWasSent();
    }

    [Fact]
    public void Send_Accepted_SaysNorviaTookItIn_WithItsId_AndClosesTheForm()
    {
        NorviaReplies(TrackingReply.Accepted("corr-1"));
        var page = OpenForm();
        FillIn(page);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=status]").TextContent.Should().Contain("Norvia accepted the Proof of delivery for CB-0000001").And.Contain("Norvia's ID for this Proof of delivery: corr-1");
            page.FindAll("section[aria-label^=\"Send Proof of delivery for \"]").Should().BeEmpty();
        });
    }

    [Fact]
    public void Send_ForADeliveredShipment_KeepsItOnTheListAsDelivered()
    {
        NorviaReplies(TrackingReply.Accepted("corr-1"));
        var page = OpenForm("CB-0000001", Shipment("CB-0000001", TrackingEventType.Delivered));
        FillIn(page);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("tbody tr").TextContent.Should().Contain("CB-0000001").And.Contain("Delivered"));
    }

    [Fact]
    public void Send_NorviaDidNotReceiveIt_WarnsAndKeepsTheFormToSendAgain()
    {
        NorviaReplies(TrackingReply.NotReceived(null));
        var page = OpenForm();
        FillIn(page);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=alert]").TextContent.Should().Contain("did not receive the Proof of delivery");
            Panel(page).Should().NotBeNull();
        });
    }

    [Fact]
    public void Send_NorviaRefusesIt_ShowsItsWords()
    {
        NorviaReplies(TrackingReply.Refused("Not a Carrier.", "corr-2"));
        var page = OpenForm();
        FillIn(page);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Not a Carrier.").And.Contain("corr-2"));
    }

    [Fact]
    public void Send_WhenThePortalFails_SaysSo_AndKeepsTheForm()
    {
        Portal.SendProofOfDeliveryAsync(Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<ProofOfDeliveryDraft>(), Arg.Any<CancellationToken>())
            .Returns<SentProofOfDelivery?>(_ => throw new InvalidOperationException("The database is down."));
        var page = OpenForm();
        FillIn(page);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=alert]").TextContent.Should().Contain("Proof of delivery couldn't be sent");
            Panel(page).Should().NotBeNull();
        });
    }

    [Fact]
    public void Send_WhenTheShipmentIsGone_IsNotFound()
    {
        Portal.SendProofOfDeliveryAsync(Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<ProofOfDeliveryDraft>(), Arg.Any<CancellationToken>())
            .Returns((SentProofOfDelivery?)null);
        var notFound = false;
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().OnNotFound += (_, _) => notFound = true;
        var page = OpenForm();
        FillIn(page);

        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => notFound.Should().BeTrue());
    }

    [Fact]
    public void Cancel_ClosesTheForm_SendingNothing()
    {
        var page = OpenForm();

        Panel(page).QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == "Cancel").Click();

        page.FindAll("section[aria-label^=\"Send Proof of delivery for \"]").Should().BeEmpty();
        NothingWasSent();
    }
}
