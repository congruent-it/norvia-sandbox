using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Web.Capacity;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Capacity;

/// <summary>The Planner's forms on the Drivers and Vehicles tabs (P10): what they send, and what they say back.</summary>
public sealed class CapacityFormsTests : TmsWebContext
{
    private static readonly DriverView Anouk = new("DRV-0001", "Anouk de Vries", [LicenceCategory.CE], [VehicleType.Van]);
    private static readonly VehicleView Truck = new("VEH-0003", "RT-201-C", VehicleType.RigidTruck, 10000, 18, LicenceCategory.C);

    [Fact]
    public void RecordAbsence_SendsTheDriverTheDaysTheReasonAndTheRemark()
    {
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Record an Absence']");
        form.QuerySelectorAll("fluent-select")[0].Change("DRV-0001");
        var fields = form.QuerySelectorAll("fluent-text-field");
        fields[0].Input("2026-10-14");
        fields[1].Input("2026-10-16");
        form.QuerySelectorAll("fluent-select")[1].Change("Training");
        fields[2].Input("ADR course");
        Commands.RecordAbsenceAsync(Arg.Any<PeriodEntry>(), Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Done());

        Click(form, "Record Absence");

        page.WaitForAssertion(() => Commands.Received(1).RecordAbsenceAsync(
            new PeriodEntry("DRV-0001", new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 16), "Training", "ADR course"), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("section[aria-label='Record an Absence'] [role=status]").TextContent.Should().Contain("recorded"));
    }

    [Fact]
    public void RecordAbsence_WithoutADriverOrDays_IsNotSent()
    {
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Record an Absence']");

        Click(form, "Record Absence");
        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Choose a driver."));

        form.QuerySelectorAll("fluent-select")[0].Change("DRV-0001");
        Click(form, "Record Absence");
        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Enter the first and the last day."));
        Commands.DidNotReceive().RecordAbsenceAsync(Arg.Any<PeriodEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecordAbsence_WhenTheTmsRefuses_ShowsItsReason()
    {
        Commands.RecordAbsenceAsync(Arg.Any<PeriodEntry>(), Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Refused("2026-10-10 is closed."));
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Record an Absence']");
        form.QuerySelectorAll("fluent-select")[0].Change("DRV-0001");
        form.QuerySelectorAll("fluent-text-field")[0].Input("2026-10-10");
        form.QuerySelectorAll("fluent-text-field")[1].Input("2026-10-10");

        Click(form, "Record Absence");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Not recorded: 2026-10-10 is closed."));
    }

    [Fact]
    public void RecordAbsence_WhenTheTmsCantBeReached_SaysToTryAgain()
    {
        Commands.RecordAbsenceAsync(Arg.Any<PeriodEntry>(), Arg.Any<CancellationToken>()).Returns<CapacityAnswer>(_ => throw new HttpRequestException("down"));
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Record an Absence']");
        form.QuerySelectorAll("fluent-select")[0].Change("DRV-0001");
        form.QuerySelectorAll("fluent-text-field")[0].Input("2026-10-14");
        form.QuerySelectorAll("fluent-text-field")[1].Input("2026-10-14");

        Click(form, "Record Absence");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("The TMS couldn't record that. Try again."));
    }

    [Fact]
    public void EditShift_SendsTheDayTheDriverAndTheTimes()
    {
        Commands.EditShiftAsync(Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Done());
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Edit a Shift']");
        form.QuerySelector("fluent-select")!.Change("DRV-0001");
        var fields = form.QuerySelectorAll("fluent-text-field");
        fields[0].Input("2026-10-14");
        fields[1].Input("05:00");
        fields[2].Input("13:00");

        Click(form, "Edit Shift");

        page.WaitForAssertion(() => Commands.Received(1).EditShiftAsync(
            new DateOnly(2026, 10, 14), "DRV-0001", new TimeOnly(5, 0), new TimeOnly(13, 0), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => form.QuerySelector("[role=status]")!.TextContent.Should().Contain("Shift set for Wed 14 Oct."));
    }

    [Fact]
    public void EditShift_WithABackwardsShift_IsNotSent()
    {
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Edit a Shift']");
        form.QuerySelector("fluent-select")!.Change("DRV-0001");
        var fields = form.QuerySelectorAll("fluent-text-field");
        fields[0].Input("2026-10-14");
        fields[1].Input("14:00");
        fields[2].Input("06:00");

        Click(form, "Edit Shift");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("with the end after the start"));
        Commands.DidNotReceive().EditShiftAsync(Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditShift_WhenTheTmsRefuses_ShowsItsReason()
    {
        Commands.EditShiftAsync(Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CapacityAnswer.Refused("Driver DRV-0001 has an Absence on 2026-10-14."));
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Edit a Shift']");
        form.QuerySelector("fluent-select")!.Change("DRV-0001");
        form.QuerySelectorAll("fluent-text-field")[0].Input("2026-10-14");

        Click(form, "Edit Shift");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Shift not set: Driver DRV-0001 has an Absence"));
    }

    [Fact]
    public void ChangeRoster_ChoosingADriver_ShowsTheirRoster_AndSendsTheTickedWeekdays()
    {
        Commands.ChangeRosterAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RosterWeekdayView>>(), Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Done());
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Change a Roster']");
        form.QuerySelector("fluent-select")!.Change("DRV-0001");

        var rows = page.WaitForElements("section[aria-label='Change a Roster'] ul[aria-label=Weekdays] li");
        rows.Should().HaveCount(7);
        rows[0].QuerySelectorAll("fluent-text-field")[0].GetAttribute("value").Should().Be("06:00");
        rows[0].QuerySelectorAll("fluent-text-field")[1].GetAttribute("value").Should().Be("14:00");
        rows[2].QuerySelectorAll("fluent-text-field")[0].HasAttribute("disabled").Should().BeTrue("Wednesday is a day off");

        rows[2].QuerySelector("fluent-checkbox")!.TriggerEvent("oncheckedchange", new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });
        rows[2].QuerySelectorAll("fluent-text-field")[0].Input("09:00");
        rows[2].QuerySelectorAll("fluent-text-field")[1].Input("17:00");
        Click(form, "Change Roster");

        page.WaitForAssertion(() => Commands.Received(1).ChangeRosterAsync(
            "DRV-0001",
            Arg.Is<IReadOnlyList<RosterWeekdayView>>(weekdays => weekdays.Select(weekday => weekday.Weekday).SequenceEqual(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday })
                && weekdays[2].Start == new TimeOnly(9, 0) && weekdays[2].End == new TimeOnly(17, 0)),
            Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => form.QuerySelector("[role=status]")!.TextContent.Should().Contain("Roster changed."));
    }

    [Fact]
    public void ChangeRoster_WithABackwardsShift_IsNotSent()
    {
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Change a Roster']");
        form.QuerySelector("fluent-select")!.Change("DRV-0001");
        var rows = page.WaitForElements("section[aria-label='Change a Roster'] ul[aria-label=Weekdays] li");
        rows[0].QuerySelectorAll("fluent-text-field")[1].Input("05:00");

        Click(form, "Change Roster");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Monday: enter a start and an end"));
        Commands.DidNotReceive().ChangeRosterAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RosterWeekdayView>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChangeRoster_WhenTheTmsRefuses_ShowsItsReason()
    {
        Commands.ChangeRosterAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RosterWeekdayView>>(), Arg.Any<CancellationToken>())
            .Returns(new CapacityAnswer.Refused("The Own fleet has no active Driver DRV-0001."));
        var page = RenderDrivers();
        var form = page.WaitForElement("section[aria-label='Change a Roster']");
        form.QuerySelector("fluent-select")!.Change("DRV-0001");
        page.WaitForElements("section[aria-label='Change a Roster'] ul[aria-label=Weekdays] li");

        Click(form, "Change Roster");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Roster not changed: The Own fleet has no active Driver"));
    }

    [Fact]
    public void RemoveAbsence_RemovesIt_AndSaysSo()
    {
        var absence = new AbsenceView(Guid.NewGuid(), "DRV-0001", "Anouk de Vries", new DateOnly(2026, 10, 14), AbsenceReason.Leave, string.Empty);
        Queries.ListAbsencesAsync(Arg.Any<CancellationToken>()).Returns([absence]);
        Commands.RemoveAbsenceAsync(absence.Id, Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Done());
        var page = RenderDrivers();

        page.WaitForElement("fluent-button[aria-label='Remove Absence of Anouk de Vries on Wed 14 Oct']").Click();

        page.WaitForAssertion(() => Commands.Received(1).RemoveAbsenceAsync(absence.Id, Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Absence of Anouk de Vries on Wed 14 Oct removed."));
    }

    [Fact]
    public void RemoveAbsence_WhenTheTmsRefuses_OrCantBeReached_SaysSo()
    {
        var absence = new AbsenceView(Guid.NewGuid(), "DRV-0001", "Anouk de Vries", new DateOnly(2026, 10, 14), AbsenceReason.Leave, string.Empty);
        Queries.ListAbsencesAsync(Arg.Any<CancellationToken>()).Returns([absence]);
        Commands.RemoveAbsenceAsync(absence.Id, Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Refused("2026-10-14 is closed."), new CapacityAnswer.Done());
        Commands.RemoveAbsenceAsync(absence.Id, Arg.Any<CancellationToken>()).Returns(
            new CapacityAnswer.Refused("2026-10-14 is closed."));
        var page = RenderDrivers();

        page.WaitForElement("fluent-button[aria-label='Remove Absence of Anouk de Vries on Wed 14 Oct']").Click();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Absence not removed: 2026-10-14 is closed."));

        Commands.RemoveAbsenceAsync(absence.Id, Arg.Any<CancellationToken>()).Returns<CapacityAnswer>(_ => throw new HttpRequestException("down"));
        page.Find("fluent-button[aria-label='Remove Absence of Anouk de Vries on Wed 14 Oct']").Click();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The TMS couldn't remove the Absence."));
    }

    [Fact]
    public void RecordOffTheRoad_SendsTheVehicleTheDaysAndTheReason()
    {
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns([Truck]);
        Commands.RecordOffTheRoadAsync(Arg.Any<PeriodEntry>(), Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Done());
        var page = Render<VehiclesTab>();
        var form = page.WaitForElement("section[aria-label='Take a Vehicle off the road']");
        page.WaitForAssertion(() => form.QuerySelectorAll("fluent-option").Should().Contain(option => option.TextContent.Contains("RT-201-C", StringComparison.Ordinal)));
        form.QuerySelectorAll("fluent-select")[0].Change("VEH-0003");
        form.QuerySelectorAll("fluent-text-field")[0].Input("2026-10-14");
        form.QuerySelectorAll("fluent-text-field")[1].Input("2026-10-14");
        form.QuerySelectorAll("fluent-select")[1].Change("Repair");

        Click(form, "Take off the road");

        page.WaitForAssertion(() => Commands.Received(1).RecordOffTheRoadAsync(
            new PeriodEntry("VEH-0003", new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), "Repair", string.Empty), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void RemoveOffTheRoad_PutsTheVehicleBack_AndSaysSo_OrShowsTheRefusal()
    {
        var day = new OffTheRoadDayView(Guid.NewGuid(), "VEH-0003", "RT-201-C", new DateOnly(2026, 10, 14), OffTheRoadReason.Repair, string.Empty);
        Queries.ListVehiclesAsync(Arg.Any<CancellationToken>()).Returns([Truck]);
        Queries.ListOffTheRoadDaysAsync(Arg.Any<CancellationToken>()).Returns([day]);
        Commands.RemoveOffTheRoadAsync(day.Id, Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Refused("closed"));
        var page = Render<VehiclesTab>();
        var button = "fluent-button[aria-label='Remove Off the road day of RT-201-C on Wed 14 Oct']";

        page.WaitForElement(button).Click();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Off the road day not removed: closed"));

        Commands.RemoveOffTheRoadAsync(day.Id, Arg.Any<CancellationToken>()).Returns(new CapacityAnswer.Done());
        page.Find(button).Click();
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("RT-201-C is back on the road on Wed 14 Oct."));

        Commands.RemoveOffTheRoadAsync(day.Id, Arg.Any<CancellationToken>()).Returns<CapacityAnswer>(_ => throw new HttpRequestException("down"));
        page.Find(button).Click();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The TMS couldn't remove the Off the road day."));
    }

    private IRenderedComponent<DriversTab> RenderDrivers()
    {
        Queries.ListDriversAsync(Arg.Any<CancellationToken>()).Returns([Anouk]);
        Queries.ListRostersAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new RosterView("DRV-0001", "Anouk de Vries",
                [new RosterWeekdayView(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(14, 0)), new RosterWeekdayView(DayOfWeek.Tuesday, new TimeOnly(6, 0), new TimeOnly(14, 0))]),
        ]);
        var page = Render<DriversTab>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Anouk de Vries"));
        return page;
    }

    private static void Click(AngleSharp.Dom.IElement scope, string label) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == label).Click();
}
