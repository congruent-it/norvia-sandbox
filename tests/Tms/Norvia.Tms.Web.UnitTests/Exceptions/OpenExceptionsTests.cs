using AngleSharp.Dom;
using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Web.Exceptions;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Exceptions;

/// <summary>The Planner's list of Open Exceptions (P7): what each shows, which actions it offers and that each one works.</summary>
public sealed class OpenExceptionsTests : TmsWebContext
{
    private static readonly CancellationToken Cancel = Xunit.TestContext.Current.CancellationToken;

    public OpenExceptionsTests()
    {
        Queries.FindShipperNotificationTemplateAsync("EX-0000005", Arg.Any<CancellationToken>()).Returns("Your delivery of SHIP-REF-1 failed.");

        Commands.NotifyShipperAsync(default!, default!, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
        Commands.CancelCarrierBookingAsync(default!, default!, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
        Commands.AgreeDeliveryWindowAsync(default!, default, default, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
        Commands.AddNoteAsync(default!, default!, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
        Commands.ResolveAsync(default!, default, default!, default).ReturnsForAnyArgs(new ExceptionAnswer.Done());
    }

    [Fact]
    public void Render_BeforeTheExceptionsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<OpenExceptionSummary>>();
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<OpenExceptions>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Open Exceptions");
    }

    [Fact]
    public void Render_WithNoOpenExceptions_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("No Exceptions are open."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_AnOpenException_ShowsItsNumberShipmentShipperCarrierTypeOpenedAtAndCauses()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed() with { NumberOfCauses = 2 }]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => CellsOf(page, "EX-0000005").Take(8).Should().Equal(
            "EX-0000005", "SH-0000012", "Brouwer Dranken", "Kessler Spedition", "Delivery failed", "Thu 24 Sep 2026, 11:45", "2", ""));
        page.Find("a[href='/shipments/SH-0000012']");
    }

    [Fact]
    public void Render_TheMarkers_ShowCauseClearedAndAfterDelivered()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            OpenExceptionSamples.DeliveryFailed() with { CauseCleared = true, AfterDelivered = true },
            OpenExceptionSamples.DeliveryFailed("EX-0000006", "SH-0000013"),
        ]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() =>
        {
            CellsOf(page, "EX-0000005")[7].Should().Be("Cause clearedAfter Delivered");
            CellsOf(page, "EX-0000006")[7].Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(null, "Not notified")]
    [InlineData(ShipperNotificationStatus.Requested, "Requested")]
    [InlineData(ShipperNotificationStatus.Sent, "Sent")]
    [InlineData(ShipperNotificationStatus.NotDelivered, "Not delivered")]
    public void Render_ShowsTheLastShipperNotificationsStatus(ShipperNotificationStatus? status, string expected)
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed() with { LastShipperNotificationStatus = status }]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => CellsOf(page, "EX-0000005")[8].Should().Be(expected));
    }

    [Fact]
    public void Render_ShowsTheNotesOfAnException()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            [OpenExceptionSamples.DeliveryFailed() with { Notes = [new ExceptionNoteSummary("Phoned Kessler", "Anna de Vries", new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero), "corr-note")] }]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => CellsOf(page, "EX-0000005")[9].Should().Contain("Phoned Kessler").And.Contain("Anna de Vries"));
    }

    [Fact]
    public void Render_ListsThemInTheOrderTheQueryGives_OldestFirst()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>())
            .Returns([OpenExceptionSamples.DeliveryFailed("EX-0000006", "SH-0000013"), OpenExceptionSamples.DeliveryFailed()]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td")[0].TextContent.Trim()).Should().Equal("EX-0000006", "EX-0000005"));
    }

    [Fact]
    public void Render_ABookedCarrierShipment_OffersNotifyCancelNoteAndResolve_ButNotAgreeingAWindow()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed()]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => ActionsOf(page).Should().Equal(
            "Notify Shipper of EX-0000005",
            "Cancel Carrier booking of EX-0000005",
            "Add note to EX-0000005",
            "Resolve EX-0000005"));
        page.Find("a[aria-label='Open timeline of EX-0000005']").GetAttribute("href").Should().Be("/shipments/SH-0000012");
    }

    [Fact]
    public void Render_ACollectedShipment_OffersAgreeingAWindow_ButNotCancelling()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            [OpenExceptionSamples.DeliveryFailed() with { CanCancelCarrierBooking = false, CanAgreeDeliveryWindow = true }]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => ActionsOf(page).Should().Equal(
            "Notify Shipper of EX-0000005",
            "Agree delivery window for EX-0000005",
            "Add note to EX-0000005",
            "Resolve EX-0000005"));
    }

    [Fact]
    public void Render_AnOwnFleetOrDeliveredShipment_OffersOnlyNotifyNoteAndResolve()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            [OpenExceptionSamples.DeliveryFailed() with { CanCancelCarrierBooking = false, CanAgreeDeliveryWindow = false }]);

        var page = Render<OpenExceptions>();

        page.WaitForAssertion(() => ActionsOf(page).Should().Equal("Notify Shipper of EX-0000005", "Add note to EX-0000005", "Resolve EX-0000005"));
    }

    [Fact]
    public void NotifyShipper_PrefillsTheTemplate_AndSendsWhatThePlannerEdited()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Notify Shipper of EX-0000005']").Click();
        var message = page.WaitForElement("section[aria-label='Notify Shipper of EX-0000005'] fluent-text-area");
        message.GetAttribute("value").Should().Be("Your delivery of SHIP-REF-1 failed.");
        message.Input("Your delivery failed. We will call you.");
        Click(Panel(page, "Notify Shipper of EX-0000005"), "Send notification");

        page.WaitForAssertion(() => Commands.Received(1).NotifyShipperAsync("EX-0000005", "Your delivery failed. We will call you.", Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("The Shipper is being notified of EX-0000005."));
        page.FindAll("section[aria-label^='Notify Shipper']").Should().BeEmpty();
    }

    [Fact]
    public void NotifyShipper_AnEmptyMessage_IsNotSent_AndSaysWhy()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Notify Shipper of EX-0000005']").Click();
        page.WaitForElement("section[aria-label='Notify Shipper of EX-0000005'] fluent-text-area").Input("   ");
        Click(Panel(page, "Notify Shipper of EX-0000005"), "Send notification");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Write the message to the Shipper."));
        Commands.DidNotReceive().NotifyShipperAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NotifyShipper_WhenTheTemplateCantBeRead_StillLetsThePlannerWriteTheMessage()
    {
        Queries.FindShipperNotificationTemplateAsync("EX-0000005", Arg.Any<CancellationToken>()).Returns<Task<string?>>(_ => throw new InvalidOperationException("down"));
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Notify Shipper of EX-0000005']").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The message couldn't be prefilled"));
        page.Find("section[aria-label='Notify Shipper of EX-0000005'] fluent-text-area");
    }

    [Fact]
    public void CancelCarrierBooking_SendsTheReason()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Cancel Carrier booking of EX-0000005']").Click();
        page.WaitForElement("section[aria-label='Cancel Carrier booking of EX-0000005'] fluent-text-area").Input("Won't make its collection");
        Click(Panel(page, "Cancel Carrier booking of EX-0000005"), "Cancel Carrier booking");

        page.WaitForAssertion(() => Commands.Received(1).CancelCarrierBookingAsync("EX-0000005", "Won't make its collection", Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("is back in the Shipments to release"));
    }

    [Fact]
    public void CancelCarrierBooking_WithoutAReason_IsNotSent()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Cancel Carrier booking of EX-0000005']").Click();
        Click(Panel(page, "Cancel Carrier booking of EX-0000005"), "Cancel Carrier booking");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Give the reason"));
        Commands.DidNotReceive().CancelCarrierBookingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AgreeDeliveryWindow_SendsTheWindowAsRotterdamTime()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            [OpenExceptionSamples.DeliveryFailed() with { CanCancelCarrierBooking = false, CanAgreeDeliveryWindow = true }]);
        var page = Render<OpenExceptions>();

        page.WaitForElement("fluent-button[aria-label='Agree delivery window for EX-0000005']").Click();
        var panel = page.WaitForElement("section[aria-label='Agree delivery window for EX-0000005']");
        panel.QuerySelectorAll("fluent-text-field")[0].Input("2026-10-08 14:30");
        panel.QuerySelectorAll("fluent-text-field")[1].Input("2026-10-08 18:30");
        Click(panel, "Agree window");

        page.WaitForAssertion(() => Commands.Received(1).AgreeDeliveryWindowAsync(
            "EX-0000005", new DateTimeOffset(2026, 10, 8, 12, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 8, 16, 30, 0, TimeSpan.Zero), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("The delivery window for EX-0000005 is agreed."));
    }

    [Theory]
    [InlineData("", "", "Give the earliest and the latest time as yyyy-MM-dd HH:mm.")]
    [InlineData("2026-10-08 14:30", "tomorrow", "Give the earliest and the latest time as yyyy-MM-dd HH:mm.")]
    [InlineData("2026-10-08 18:30", "2026-10-08 14:30", "The earliest time can't be after the latest.")]
    public void AgreeDeliveryWindow_AWindowThatIsntOne_IsNotSent_AndSaysWhy(string earliest, string latest, string message)
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(
            [OpenExceptionSamples.DeliveryFailed() with { CanCancelCarrierBooking = false, CanAgreeDeliveryWindow = true }]);
        var page = Render<OpenExceptions>();

        page.WaitForElement("fluent-button[aria-label='Agree delivery window for EX-0000005']").Click();
        var panel = page.WaitForElement("section[aria-label='Agree delivery window for EX-0000005']");
        panel.QuerySelectorAll("fluent-text-field")[0].Input(earliest);
        panel.QuerySelectorAll("fluent-text-field")[1].Input(latest);
        Click(panel, "Agree window");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain(message));
        Commands.DidNotReceive().AgreeDeliveryWindowAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddNote_SendsTheNote()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Add note to EX-0000005']").Click();
        page.WaitForElement("section[aria-label='Add note to EX-0000005'] fluent-text-area").Input("Phoned Kessler, redelivery Thursday");
        Click(Panel(page, "Add note to EX-0000005"), "Add note");

        page.WaitForAssertion(() => Commands.Received(1).AddNoteAsync("EX-0000005", "Phoned Kessler, redelivery Thursday", Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("The note is added to EX-0000005."));
    }

    [Fact]
    public void Resolve_SendsTheOutcomeAndTheRemark()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Resolve EX-0000005']").Click();
        var panel = page.WaitForElement("section[aria-label='Resolve EX-0000005']");
        panel.QuerySelector("fluent-select")!.Change("Redelivered");
        panel.QuerySelector("fluent-text-area")!.Input("Delivered on Thursday");
        Click(panel, "Resolve Exception");

        page.WaitForAssertion(() => Commands.Received(1).ResolveAsync("EX-0000005", ExceptionOutcome.Redelivered, "Delivered on Thursday", Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("EX-0000005 is resolved."));
    }

    [Fact]
    public void Resolve_WithoutAnOutcomeOrARemark_IsNotSent()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Resolve EX-0000005']").Click();
        var panel = page.WaitForElement("section[aria-label='Resolve EX-0000005']");
        Click(panel, "Resolve Exception");
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Choose the outcome."));

        panel.QuerySelector("fluent-select")!.Change("Replanned");
        Click(panel, "Resolve Exception");
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Give a remark"));
        Commands.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<ExceptionOutcome>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnAction_TheTmsRefuses_KeepsTheFormOpen_WithTheReason()
    {
        Commands.AddNoteAsync("EX-0000005", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ExceptionAnswer.Refused("Exception EX-0000005 is Resolved, not Open."));
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Add note to EX-0000005']").Click();
        page.WaitForElement("section[aria-label='Add note to EX-0000005'] fluent-text-area").Input("Phoned Kessler");
        Click(Panel(page, "Add note to EX-0000005"), "Add note");

        page.WaitForAssertion(() => page.Find("section[aria-label='Add note to EX-0000005'] [role=alert]").TextContent.Should().Contain("Exception EX-0000005 wasn't changed: Exception EX-0000005 is Resolved, not Open."));
    }

    [Fact]
    public void AnAction_WhenTheTmsApiCantBeReached_SaysToTryAgain()
    {
        Commands.AddNoteAsync("EX-0000005", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<ExceptionAnswer>>(_ => throw new HttpRequestException("down"));
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Add note to EX-0000005']").Click();
        page.WaitForElement("section[aria-label='Add note to EX-0000005'] fluent-text-area").Input("Phoned Kessler");
        Click(Panel(page, "Add note to EX-0000005"), "Add note");

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Exception EX-0000005 couldn't be changed. Try again."));
    }

    [Fact]
    public void TheForm_CanBeClosedWithoutActing()
    {
        var page = RenderWithOne();

        page.Find("fluent-button[aria-label='Add note to EX-0000005']").Click();
        Click(Panel(page, "Add note to EX-0000005"), "Cancel");

        page.WaitForAssertion(() => page.FindAll("section[aria-label^='Add note']").Should().BeEmpty());
    }

    [Fact]
    public void AnExceptionResolvedMeanwhile_TakesItsFormWithItWhenTheListRefreshes()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed()]);
        var page = Render<OpenExceptions>();
        page.WaitForElement("fluent-button[aria-label='Add note to EX-0000005']").Click();
        page.WaitForElement("section[aria-label='Add note to EX-0000005']");
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([]);

        // Time moves on until the list has read again, as the refresh's timer starts only once its first read is done.
        page.WaitForAssertion(() =>
        {
            Time.Advance(RefreshInterval);
            page.FindAll("section[aria-label^='Add note']").Should().BeEmpty();
        });
        page.Find("[role=status]").TextContent.Should().Contain("No Exceptions are open.");
    }

    [Fact]
    public void Render_ANewExceptionThatArrivesMeanwhile_AppearsWithoutARefresh()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed()]);
        var page = Render<OpenExceptions>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed(), OpenExceptionSamples.DeliveryFailed("EX-0000006", "SH-0000013")]);

        // Time moves on until the list has read again, as the refresh's timer starts only once its first read is done.
        page.WaitForAssertion(() =>
        {
            Time.Advance(RefreshInterval);
            page.FindAll("a[href='/shipments/SH-0000013']").Should().NotBeEmpty();
        });
    }

    private IRenderedComponent<OpenExceptions> RenderWithOne()
    {
        Queries.ListOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns([OpenExceptionSamples.DeliveryFailed()]);
        var page = Render<OpenExceptions>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");
        return page;
    }

    private static void Click(IElement panel, string text) =>
        panel.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == text).Click();

    private static IElement Panel(IRenderedComponent<OpenExceptions> page, string name) => page.WaitForElement($"section[aria-label='{name}']");

    private static List<string> ActionsOf(IRenderedComponent<OpenExceptions> page) =>
        [.. page.FindAll("tbody fluent-button, tbody a[aria-label]").Select(action => action.GetAttribute("aria-label")!).Where(label => !label.StartsWith("Open timeline", StringComparison.Ordinal))];

    private static List<string> CellsOf(IRenderedComponent<OpenExceptions> page, string exceptionNumber) =>
        [.. page.FindAll("tbody tr").Single(row => row.QuerySelectorAll("td")[0].TextContent.Trim() == exceptionNumber)
            .QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];
}
