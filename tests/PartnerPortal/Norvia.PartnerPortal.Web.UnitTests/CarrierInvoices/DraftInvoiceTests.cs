using Ardalis.Result;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Application.CarrierInvoices.Draft;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierInvoices;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierInvoices;

public sealed class DraftInvoiceTests : PortalWebContext
{
    private static readonly PersonaId Carrier = new("van-dijk-transport");

    private static CarrierInvoiceDraftView Draft(params CarrierInvoiceDraftLine[] lines) =>
        new("INV-20260925-001", new DateOnly(2026, 9, 25), lines);

    private static readonly CarrierInvoiceDraftLine First = new("VDT-1", "CB-0000001", "SH-0000001", 120.5m);
    private static readonly CarrierInvoiceDraftLine Second = new("VDT-2", "CB-0000002", "SH-0000002", null);

    private static SubmittedCarrierInvoice Sent(CarrierInvoiceReply reply, params CarrierInvoiceLineView[] lines) =>
        new("INV-20260925-001", reply, lines.Length == 0
            ? null
            : new CarrierInvoiceView("INV-20260925-001", new DateOnly(2026, 9, 25), Samples.SentAt, lines.Sum(line => line.Amount), CarrierInvoiceStatus.Received, null, "corr-1", lines, null));

    private static CarrierInvoiceReply Received() =>
        CarrierInvoiceReply.Received("corr-1", new CarrierInvoiceReceipt(false, CarrierInvoiceStatus.Received, null, [new("VDT-1", 120.5m, CarrierInvoiceLineResult.PriceMismatch)]));

    private IRenderedComponent<DraftInvoice> RenderDraft(params CarrierInvoiceDraftLine[] lines)
    {
        Portal.GetCarrierInvoiceDraftAsync(Carrier, Arg.Any<CancellationToken>()).Returns(Draft(lines));
        return Render<DraftInvoice>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));
    }

    [Fact]
    public void Render_ListsTheDeliveredBookings_WithTheAgreedPrice_AndAnEditableAmount()
    {
        var page = RenderDraft(First, Second);

        var rows = page.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("CB-0000001").And.Contain("SH-0000001").And.Contain("VDT-1").And.Contain("€ 120.50");
        rows[1].TextContent.Should().Contain("VDT-2").And.Contain("None");
        page.Find("[aria-label='Amount for VDT-1']").GetAttribute("current-value").Should().Be("120.50");
        page.Markup.Should().Contain("Invoice number").And.Contain("INV-20260925-001").And.Contain("25-09-2026");
    }

    [Fact]
    public void Render_NeedsNoCallToNorvia_ToListTheBookings()
    {
        RenderDraft(First);

        Portal.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Should().NotContain(["SubmitCarrierInvoiceAsync"]);
    }

    [Fact]
    public void Render_WithNothingToInvoice_SaysSo()
    {
        var page = RenderDraft();

        page.Find("[role=status]").TextContent.Should().Contain("has nothing to invoice");
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Send_SendsTheSelectedBookings_WithTheirEditedAmounts_AsThePersona_AndShowsEachLinesResult()
    {
        Portal.SubmitCarrierInvoiceAsync(Carrier, Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Sent(Received(), new CarrierInvoiceLineView("VDT-1", 99m, null, CarrierInvoiceLineResult.PriceMismatch, null))));
        var page = RenderDraft(First, Second);
        page.Find("[aria-label='Amount for VDT-1']").Change("99.00");
        page.Find("[aria-label='Invoice VDT-2']").Change(false);

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        Portal.Received(1).SubmitCarrierInvoiceAsync(
            Carrier, "INV-20260925-001", new DateOnly(2026, 9, 25),
            Arg.Is<IReadOnlyList<CarrierInvoiceLineDraft>>(lines => lines.Count == 1 && lines[0].BookingRef == "VDT-1" && lines[0].Amount == 99.00m),
            Arg.Any<CancellationToken>());
        page.Find("[role=status]").TextContent.Should().Contain("Norvia received invoice INV-20260925-001");
        page.Find("section[aria-label='Result of invoice INV-20260925-001']").TextContent.Should().Contain("VDT-1").And.Contain("€ 99.00").And.Contain("Price mismatch");
    }

    [Fact]
    public void Send_WhenNorviaReceivedIt_BuildsTheDraftAgain()
    {
        Portal.SubmitCarrierInvoiceAsync(Carrier, Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Sent(Received())));
        Portal.GetCarrierInvoiceDraftAsync(Carrier, Arg.Any<CancellationToken>()).Returns(Draft(First, Second), Draft(Second));
        var page = Render<DraftInvoice>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));
        page.Find("[aria-label='Invoice VDT-2']").Change(false);

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("VDT-2"));
    }

    [Fact]
    public void Send_ARefusedInvoice_KeepsTheDraft_AndSaysWhy()
    {
        Portal.SubmitCarrierInvoiceAsync(Carrier, Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Sent(CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.InvoiceNumberUsed, "Use a new number.", "corr-3"))));
        var page = RenderDraft(First);

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("Use a new number.").And.Contain("corr-3");
        page.FindAll("tbody tr").Should().ContainSingle();
        Portal.Received(1).GetCarrierInvoiceDraftAsync(Carrier, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Send_AnAmountThatIsNoNumber_IsNotSent()
    {
        var page = RenderDraft(First);
        page.Find("[aria-label='Amount for VDT-1']").Change("lots");

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("amount for VDT-1 must be a number");
        Portal.DidNotReceive().SubmitCarrierInvoiceAsync(
            Arg.Any<PersonaId>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Send_WhatTheApplicationFindsInvalid_IsShownAsItSaysIt()
    {
        Portal.SubmitCarrierInvoiceAsync(Carrier, Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SubmittedCarrierInvoice>.Invalid(new ValidationError("A Carrier invoice needs at least one line.")));
        var page = RenderDraft(First);
        page.Find("[aria-label='Invoice VDT-1']").Change(false);

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("needs at least one line");
    }

    [Fact]
    public void Send_WhenThePortalItselfFails_SaysSo()
    {
        Portal.SubmitCarrierInvoiceAsync(Carrier, Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns<Result<SubmittedCarrierInvoice>>(_ => throw new InvalidOperationException("The database is down."));
        var page = RenderDraft(First);

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        page.Find("[role=alert]").TextContent.Should().Contain("couldn't be sent or kept");
    }

    [Fact]
    public void Send_ForAPersonaThatWentAway_IsNotFound()
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;
        Portal.SubmitCarrierInvoiceAsync(Carrier, Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SubmittedCarrierInvoice>.NotFound());
        var page = RenderDraft(First);

        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Send invoice").Click();

        notFound.Should().BeTrue();
    }

    [Fact]
    public void Render_WhenTheDraftCantBeLoaded_SaysSo()
    {
        Portal.GetCarrierInvoiceDraftAsync(Carrier, Arg.Any<CancellationToken>())
            .Returns<CarrierInvoiceDraftView?>(_ => throw new InvalidOperationException("The database is down."));

        Render<DraftInvoice>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"))
            .Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("brouwer-dranken")]
    public void Render_ForAPersonaThatIsNoCarrier_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<DraftInvoice>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }

    [Fact]
    public void Render_WhenTheApplicationHasNoDraft_IsNotFound()
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;
        Portal.GetCarrierInvoiceDraftAsync(Carrier, Arg.Any<CancellationToken>()).Returns((CarrierInvoiceDraftView?)null);

        Render<DraftInvoice>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));

        notFound.Should().BeTrue();
    }
}
