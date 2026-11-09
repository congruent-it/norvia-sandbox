using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.ShipperInvoices;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperInvoices;
using Norvia.PartnerPortal.Web.ShipperInvoices;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperInvoices;

public sealed class InvoicesInboxTests : PortalWebContext
{
    private static ShipperInvoiceView Invoice(string number = "INV-2030-000001", DateTimeOffset? at = null) => new(
        number, InvoiceDocumentType.Invoice, at ?? new DateTimeOffset(2030, 11, 1, 5, 0, 0, TimeSpan.Zero), new DateOnly(2030, 11, 1), null,
        [
            new ShipperInvoiceLine("TO-0000001", "BD-2030-0001", new DateOnly(2030, 10, 18), [new ShipperChargeLine(ChargeCode.Freight, 80.00m)], 80.00m),
            new ShipperInvoiceLine(
                "TO-0000002", "BD-2030-0002", new DateOnly(2030, 10, 21),
                [new ShipperChargeLine(ChargeCode.Freight, 35.50m), new ShipperChargeLine(ChargeCode.MinimumChargeAdjustment, 14.50m)], 50.00m),
        ],
        1130.00m, 237.30m, 1367.30m, "corr-1");

    private static ShipperInvoiceView CreditNote() => Invoice("CN-2030-000001") with
    {
        DocumentType = InvoiceDocumentType.CreditNote,
        Credit = new CreditedLine("INV-2030-000001", "TO-0000002", "Rate Card error"),
    };

    [Fact]
    public void Render_ShowsEachInvoice_WithItsLinesAndTotals()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns([Invoice()]);

        var page = RenderPage();

        var article = page.Find("article[aria-label='INV-2030-000001']");
        article.TextContent.Should().Contain("Invoice INV-2030-000001").And.Contain("01-11-2030");
        var rows = article.QuerySelectorAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("TO-0000001").And.Contain("BD-2030-0001").And.Contain("18-10-2030").And.Contain("Freight € 80.00").And.Contain("€ 80.00");
        rows[1].TextContent.Should().Contain("Freight € 35.50, Minimum charge adjustment € 14.50").And.Contain("€ 50.00");
        article.QuerySelector(".total-without-vat")!.TextContent.Should().Be("€ 1,130.00");
        article.QuerySelector(".vat")!.TextContent.Should().Be("€ 237.30");
        article.QuerySelector(".total-with-vat")!.TextContent.Should().Be("€ 1,367.30");
    }

    [Fact]
    public void Render_ACreditNote_SaysWhatItCredits()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns([CreditNote()]);

        var article = RenderPage().Find("article[aria-label='CN-2030-000001']");

        article.TextContent.Should().Contain("Credit note CN-2030-000001").And.Contain("Credits INV-2030-000001, TO-0000002: Rate Card error");
    }

    [Fact]
    public void Render_AnInvoice_CreditsNothing() =>
        InvoiceText.Credits(null).Should().BeEmpty();

    [Fact]
    public void Render_KeepsTheOrderTheApplicationGives_NewestFirst()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns([CreditNote(), Invoice()]);

        var documents = RenderPage().FindAll("article");

        documents.Should().HaveCount(2);
        documents[0].GetAttribute("aria-label").Should().Be("CN-2030-000001");
        documents[1].GetAttribute("aria-label").Should().Be("INV-2030-000001");
    }

    [Fact]
    public void Render_WithNoInvoices_SaysSo()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns([]);

        RenderPage().Find("[role=status]").TextContent.Should().Contain("hasn't sent Brouwer Dranken an invoice yet");
    }

    [Fact]
    public void Render_AsInvoicesArrive_ShowsThemWithoutAReload()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns([], [Invoice("INV-2030-000007")]);
        var page = RenderPage();
        page.Find("[role=status]").TextContent.Should().Contain("hasn't sent");

        Time.Advance(InvoicesInbox.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("article").Should().ContainSingle().Which.GetAttribute("aria-label").Should().Be("INV-2030-000007"));
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsWhatItShowed_AndTriesAgain()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns(
            _ => [Invoice("INV-2030-000001")],
            _ => throw new InvalidOperationException("The database is down."),
            _ => [Invoice("INV-2030-000001"), Invoice("INV-2030-000002")]);
        var page = RenderPage();

        Time.Advance(InvoicesInbox.RefreshInterval);
        // Ticks that come while a refresh is still running are one tick, so the next one waits for the failed read.
        page.WaitForAssertion(() => Portal.Received(2).ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>()));
        page.FindAll("article").Should().ContainSingle();
        Time.Advance(InvoicesInbox.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("article").Should().HaveCount(2));
    }

    [Fact]
    public void Render_WhenTheInboxCantBeLoaded_SaysSo()
    {
        Portal.ListShipperInvoicesAsync(new PersonaId("brouwer-dranken"), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ShipperInvoiceView>>(_ => throw new InvalidOperationException("The database is down."));

        RenderPage().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("van-dijk-transport")]
    public void Render_ForAPersonaThatIsNoShipper_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<InvoicesInbox>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }

    [Theory]
    [InlineData(InvoiceDocumentType.Invoice, "Invoice")]
    [InlineData(InvoiceDocumentType.CreditNote, "Credit note")]
    public void InvoiceText_NamesTheDocumentType(InvoiceDocumentType type, string text) =>
        InvoiceText.Of(type).Should().Be(text);

    [Theory]
    [InlineData(ChargeCode.Freight, "Freight")]
    [InlineData(ChargeCode.MinimumChargeAdjustment, "Minimum charge adjustment")]
    public void InvoiceText_NamesTheChargeCode(ChargeCode code, string text) =>
        InvoiceText.Of(code).Should().Be(text);

    [Fact]
    public void InvoiceText_ForAValueItDoesNotKnow_Throws()
    {
        ((Func<string>)(() => InvoiceText.Of((InvoiceDocumentType)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Func<string>)(() => InvoiceText.Of((ChargeCode)99))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Money_IsWrittenInEurosWithTwoDecimals() =>
        InvoiceText.Money(1234.5m).Should().Be("€ 1,234.50");

    private IRenderedComponent<InvoicesInbox> RenderPage() =>
        Render<InvoicesInbox>(parameters => parameters.Add(page => page.PersonaId, "brouwer-dranken"));
}
