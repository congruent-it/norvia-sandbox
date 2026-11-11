using System.Text.Json;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.Exceptions.Timeline;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The Planner agent's view of an Exception is its timeline and nothing more: staff-side facts, no postcode, no Consignee, no file (#443, ADR 0040).</summary>
public sealed class PlannerAgentExceptionTimelineViewTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

    private static ExceptionTimelineForAgent Timeline() => new(
        "SH-0000012",
        ShipmentStatus.Booked,
        new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
        new ExceptionTimelineEntry(
            "EX-0000005",
            ExceptionType.DeliveryFailed,
            ExceptionStatus.Open,
            Now.AddHours(-3),
            AfterDelivered: false,
            CauseCleared: false,
            [new ExceptionCauseView(ExceptionCauseKind.TrackingEvent, "evt-ndl", Now.AddHours(-3))],
            [new ExceptionNoteView("Phoned Van Dijk, they retry tomorrow", "Anna de Vries", Now.AddHours(-1))],
            [new ShipperNotificationView("Your delivery failed.", "Anna de Vries", ShipperNotificationStatus.Sent, Now.AddHours(-2), Now.AddHours(-2), null)],
            [new CarrierBookingCancellationView("CB-0000042", "Carrier cannot retry", Now.AddMinutes(-30), CarrierNotTold: true, "503 from the Carrier")],
            null,
            null,
            null),
        [new TrackingTimelineEntry(TrackingEventType.DeliveryFailed, Now.AddHours(-3), "Rotterdam", "3011 AA", "NL", null, "Consignee closed", Now.AddHours(-3), true, "CAR-0001")],
        [new CarrierBookingView(
            "CB-0000042", new CarrierView("CAR-0001", "Van Dijk Transport"), CarrierBookingStatus.Confirmed, Now.AddDays(-1), Now.AddDays(-1).AddHours(1),
            "VDT-REF-9", null, null, null, null, null, null, null)],
        [new AgreedDeliveryWindowView(Now.AddDays(1), Now.AddDays(1).AddHours(4), "EX-0000005", Now.AddHours(-1))]);

    [Fact]
    public void Of_IsTheExceptionItsCausesItsStatusAndTheShipmentAndWhoCarriesIt()
    {
        var view = PlannerAgentMapping.Of(Timeline(), Now);

        view.ExceptionNumber.Should().Be("EX-0000005");
        view.ShipmentNumber.Should().Be("SH-0000012");
        view.Type.Should().Be("DeliveryFailed");
        view.Status.Should().Be("Open");
        view.ShipmentStatus.Should().Be("Booked");
        view.CarriedBy.Should().Be("Van Dijk Transport");
        view.Causes.Should().Equal(new PlannerAgentCauseView("TrackingEvent", Now.AddHours(-3)));
        view.AsOf.Should().Be(Now);
    }

    [Fact]
    public void Of_OwnFleet_IsCarriedByOwnFleet()
    {
        var view = PlannerAgentMapping.Of(Timeline() with { CarriedBy = new CarriedByView(AssignedTo.OwnFleet, null) }, Now);

        view.CarriedBy.Should().Be("Own fleet");
    }

    [Fact]
    public void Of_TheTrackingEventsKeepTheirSourceAndTheReportersRemark_ButNotThePostcode()
    {
        var view = PlannerAgentMapping.Of(Timeline(), Now);

        view.TrackingEvents.Should().Equal(
            new PlannerAgentTrackingEventView("DeliveryFailed", Now.AddHours(-3), "Rotterdam", "NL", null, "Consignee closed", "CAR-0001", true));
    }

    [Fact]
    public void Of_TheActionsAlreadyTaken_AreTheNotificationsTheCancellationsTheAgreedWindowsAndTheBookingState()
    {
        var view = PlannerAgentMapping.Of(Timeline(), Now);

        view.ShipperNotifications.Should().Equal(new PlannerAgentShipperNotificationView("Your delivery failed.", "Sent", Now.AddHours(-2), Now.AddHours(-2)));
        view.CarrierBookingCancellations.Should().Equal(new PlannerAgentCancellationView("CB-0000042", "Carrier cannot retry", Now.AddMinutes(-30), true));
        view.AgreedDeliveryWindows.Should().Equal(new PlannerAgentAgreedWindowView(Now.AddDays(1), Now.AddDays(1).AddHours(4), Now.AddHours(-1)));
        view.CarrierBookings.Should().Equal(new PlannerAgentCarrierBookingView("CB-0000042", "Van Dijk Transport", "Confirmed", Now.AddDays(-1), Now.AddDays(-1).AddHours(1), null, null));
    }

    [Fact]
    public void Of_TheNotesSoFar_AreTheirTextWhoWroteThemAndWhen()
    {
        var view = PlannerAgentMapping.Of(Timeline(), Now);

        view.Notes.Should().Equal(new PlannerAgentNoteView("Phoned Van Dijk, they retry tomorrow", "Anna de Vries", Now.AddHours(-1)));
    }

    [Fact]
    public void Of_HasNoPostcodeNoBookingReferenceNoConsigneeAndNoFile()
    {
        var json = JsonSerializer.Serialize(PlannerAgentMapping.Of(Timeline(), Now), Web);

        json.Should().NotContain("3011 AA").And.NotContain("VDT-REF-9")
            .And.NotContainEquivalentOf("\"postcode\":").And.NotContainEquivalentOf("\"consignee\":").And.NotContainEquivalentOf("\"file\":").And.NotContainEquivalentOf("\"street\":");
    }
}
