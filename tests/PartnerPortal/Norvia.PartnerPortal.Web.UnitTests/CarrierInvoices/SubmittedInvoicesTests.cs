using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierInvoices;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierInvoices;

public sealed class SubmittedInvoicesTests : PortalWebContext
{
    private static readonly PersonaId Carrier = new("van-dijk-transport");

    private static CarrierInvoiceView Waiting(string number = "INV-1") => new(
        number, new DateOnly(2026, 9, 25), Samples.SentAt, 210m, CarrierInvoiceStatus.Received, null, "corr-1",
        [
            new("VDT-1", 100m, null, CarrierInvoiceLineResult.Matched, null),
            new("VDT-2", 110m, null, CarrierInvoiceLineResult.PriceMismatch, null),
        ],
        OutcomeReceivedAt: null);

    private static CarrierInvoiceView Told(string number = "INV-2") => new(
        number, new DateOnly(2026, 9, 25), Samples.SentAt, 210m, CarrierInvoiceStatus.Approved, 190m, "corr-1",
        [
            new("VDT-1", 100m, null, CarrierInvoiceLineResult.Matched, new CarrierInvoiceOutcomeLine("VDT-1", CarrierInvoiceOutcomeResult.Matched, 100m, null)),
            new("VDT-2", 110m, null, CarrierInvoiceLineResult.PriceMismatch, new CarrierInvoiceOutcomeLine("VDT-2", CarrierInvoiceOutcomeResult.AcceptedAdjusted, 90m, "Waiting time")),
            new("VDT-3", 0m, null, CarrierInvoiceLineResult.Duplicate, new CarrierInvoiceOutcomeLine("VDT-3", CarrierInvoiceOutcomeResult.Rejected, 0m, "Invoiced already")),
        ],
        OutcomeReceivedAt: Samples.SentAt.AddHours(1));

    private IRenderedComponent<SubmittedInvoices> RenderPage() =>
        Render<SubmittedInvoices>(parameters => parameters.Add(page => page.PersonaId, "van-dijk-transport"));

    [Fact]
    public void Render_ShowsEachInvoice_WithWhatNorviaMadeOfEachLine_AndWaitsForTheOutcome()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>()).Returns([Waiting()]);

        var article = RenderPage().Find("article[aria-label='INV-1']");

        article.TextContent.Should().Contain("Invoice INV-1").And.Contain("25-09-2026").And.Contain("Received");
        article.QuerySelector(".told").Should().BeNull();
        var rows = article.QuerySelectorAll("tbody tr");
        rows[0].TextContent.Should().Contain("VDT-1").And.Contain("€ 100.00").And.Contain("Matched").And.Contain("Waiting for Norvia");
        rows[1].TextContent.Should().Contain("Price mismatch").And.Contain("Waiting for Norvia");
        article.QuerySelector(".total")!.TextContent.Should().Be("€ 210.00");
        article.QuerySelector(".approved").Should().BeNull();
    }

    [Fact]
    public void Render_AnInvoiceToldTheOutcome_ShowsTheOutcomePerLine_TheRemarks_AndTheApprovedAmount()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>()).Returns([Told()]);

        var article = RenderPage().Find("article[aria-label='INV-2']");

        article.QuerySelector(".status")!.TextContent.Should().Be("Approved");
        article.QuerySelector(".told").Should().NotBeNull();
        var outcomes = article.QuerySelectorAll("td.outcome").Select(cell => cell.TextContent).ToList();
        outcomes.Should().Equal("Matched at € 100.00", "Accepted, adjusted at € 90.00", "Rejected");
        article.TextContent.Should().Contain("Waiting time").And.Contain("Invoiced already");
        article.QuerySelector(".approved")!.TextContent.Should().Be("€ 190.00");
    }

    [Fact]
    public void Render_KeepsTheOrderTheApplicationGives_NewestFirst()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>()).Returns([Told("INV-2"), Waiting("INV-1")]);

        RenderPage().FindAll("article").Select(article => article.GetAttribute("aria-label")).Should().Equal("INV-2", "INV-1");
    }

    [Fact]
    public void Render_WithNoInvoices_SaysSo()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>()).Returns([]);

        RenderPage().Find("[role=status]").TextContent.Should().Contain("hasn't sent Norvia an invoice yet");
    }

    [Fact]
    public void Render_WhenTheOutcomeArrives_ShowsItWithoutAReload()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>()).Returns([Waiting("INV-2")], [Told("INV-2")]);
        var page = RenderPage();
        page.Find("td.outcome").TextContent.Should().Be("Waiting for Norvia");

        Time.Advance(SubmittedInvoices.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("td.outcome")[0].TextContent.Should().Be("Matched at € 100.00"));
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsWhatItShowed_AndTriesAgain()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>()).Returns(
            _ => [Waiting("INV-1")],
            _ => throw new InvalidOperationException("The database is down."),
            _ => [Waiting("INV-1"), Waiting("INV-2")]);
        var page = RenderPage();

        Time.Advance(SubmittedInvoices.RefreshInterval);
        page.FindAll("article").Should().ContainSingle();
        Time.Advance(SubmittedInvoices.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("article").Should().HaveCount(2));
    }

    [Fact]
    public void Render_WhenTheInvoicesCantBeLoaded_SaysSo()
    {
        Portal.ListCarrierInvoicesAsync(Carrier, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CarrierInvoiceView>>(_ => throw new InvalidOperationException("The database is down."));

        RenderPage().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("brouwer-dranken")]
    public void Render_ForAPersonaThatIsNoCarrier_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<SubmittedInvoices>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }
}
