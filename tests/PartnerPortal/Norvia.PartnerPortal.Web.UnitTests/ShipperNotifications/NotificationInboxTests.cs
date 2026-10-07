using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.ShipperNotifications;
using Norvia.PartnerPortal.Domain.ShipperNotifications;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperNotifications;

public sealed class NotificationInboxTests : PortalWebContext
{
    private static ShipperNotificationView Notification(
        string order = "TO-0000042", string reference = "BD-2030-0001", DateTimeOffset? at = null, DateTime? eta = null, DateTime? from = null, DateTime? until = null) => new(
        Guid.CreateVersion7(), at ?? new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), order, reference, ShipperExceptionCode.Late,
        "The truck is stuck in traffic.", Timing(eta, from, until), "Eva Planner", "corr-1");

    private static DeliveryTiming Timing(DateTime? eta, DateTime? from, DateTime? until) =>
        from is { } start && until is { } end ? DeliveryTiming.WithWindow(start, end)
        : eta is { } expected ? DeliveryTiming.WithEta(expected)
        : DeliveryTiming.None;

    [Fact]
    public void Render_ListsEveryNotification_WithItsTransportOrderNumberAndShipperReference()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns([Notification(eta: new DateTime(2030, 10, 3, 14, 30, 0))]);

        var row = RenderPage().FindAll("tbody tr").Should().ContainSingle().Subject;

        row.TextContent.Should().Contain("Wed 7 Oct 2026, 11:00").And.Contain("TO-0000042").And.Contain("BD-2030-0001")
            .And.Contain("Delivery late").And.Contain("The truck is stuck in traffic.").And.Contain("ETA 03-10-2030 14:30").And.Contain("Eva Planner");
    }

    [Fact]
    public void Render_KeepsTheOrderTheApplicationGives_NewestFirst()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns(
            [Notification("TO-0000002", at: new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero)), Notification("TO-0000001", at: new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero))]);

        var rows = RenderPage().FindAll("tbody tr");

        rows.Select(row => row.TextContent).Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("TO-0000002");
        rows[1].TextContent.Should().Contain("TO-0000001");
    }

    [Fact]
    public void Render_ANotificationWithAnAgreedWindow_ShowsTheWindowInsteadOfTheEta()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>())
            .Returns([Notification(from: new DateTime(2030, 10, 4, 9, 0, 0), until: new DateTime(2030, 10, 4, 12, 0, 0))]);

        RenderPage().Find("tbody tr").TextContent.Should().Contain("Agreed window 04-10-2030 09:00 to 04-10-2030 12:00").And.NotContain("ETA");
    }

    [Fact]
    public void Render_WithNoNotifications_SaysSo()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns([]);

        RenderPage().Find("[role=status]").TextContent.Should().Contain("hasn't sent Brouwer Dranken a notification yet");
    }

    [Fact]
    public void Render_AsNotificationsArrive_ShowsThemWithoutAReload()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns([], [Notification("TO-0000007")]);
        var page = RenderPage();
        page.Find("[role=status]").TextContent.Should().Contain("hasn't sent");

        Time.Advance(NotificationInbox.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("TO-0000007"));
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsWhatItShowed_AndTriesAgain()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>()).Returns(
            _ => [Notification("TO-0000001")],
            _ => throw new InvalidOperationException("The database is down."),
            _ => [Notification("TO-0000001"), Notification("TO-0000002")]);
        var page = RenderPage();

        Time.Advance(NotificationInbox.RefreshInterval);
        page.FindAll("tbody tr").Should().ContainSingle();
        Time.Advance(NotificationInbox.RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(2));
    }

    [Fact]
    public void Render_WhenTheInboxCantBeLoaded_SaysSo()
    {
        Portal.ListShipperNotificationsAsync("brouwer-dranken", Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ShipperNotificationView>>(_ => throw new InvalidOperationException("The database is down."));

        RenderPage().Find("[role=alert]").TextContent.Should().Contain("couldn't be loaded");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("van-dijk-transport")]
    public void Render_ForAPersonaThatIsNoShipper_IsNotFound(string personaId)
    {
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<NotificationInbox>(parameters => parameters.Add(page => page.PersonaId, personaId));

        notFound.Should().BeTrue();
    }

    [Fact]
    public void Arrival_ANotificationWithNeither_SaysNothing() =>
        ExceptionText.Arrival(Notification()).Should().BeEmpty();

    [Fact]
    public void ExceptionText_HasWordsForEveryCode() =>
        Enum.GetValues<ShipperExceptionCode>().Select(ExceptionText.Of).Should().OnlyHaveUniqueItems().And.OnlyContain(text => text.Length > 0);

    [Fact]
    public void ExceptionText_ForACodeItDoesNotKnow_Throws() =>
        ((Func<string>)(() => ExceptionText.Of((ShipperExceptionCode)99))).Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData(ShipperExceptionCode.CollectionFailed, "Collection failed")]
    [InlineData(ShipperExceptionCode.DeliveryFailed, "Delivery failed")]
    [InlineData(ShipperExceptionCode.Damaged, "Goods damaged")]
    [InlineData(ShipperExceptionCode.Reservations, "Delivered with reservations")]
    [InlineData(ShipperExceptionCode.Late, "Delivery late")]
    public void ExceptionText_NamesWhatItIsAbout(ShipperExceptionCode code, string text) =>
        ExceptionText.Of(code).Should().Be(text);

    private IRenderedComponent<NotificationInbox> RenderPage() =>
        Render<NotificationInbox>(parameters => parameters.Add(page => page.PersonaId, "brouwer-dranken"));
}
