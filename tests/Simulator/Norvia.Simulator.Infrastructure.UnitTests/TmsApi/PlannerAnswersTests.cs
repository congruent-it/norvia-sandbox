using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.TmsApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.TmsApi;

public sealed class PlannerAnswersTests
{
    [Theory]
    [InlineData(201)]
    [InlineData(200)]
    public void Assigned_APlannedShipment_GivesItsNumberAndTheTrailOfTheAssignment(int status)
    {
        var answer = PlannerAnswers.Assigned(status, """{"shipmentNumber":"SH-0000007","transportOrderNumber":"TO-0000042","correlationId":"plan-1"}""", "header-1");

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.Planned, "plan-1", "SH-0000007", null));
    }

    [Fact]
    public void Assigned_NoCorrelationIdInTheBody_UsesTheHeader()
    {
        PlannerAnswers.Assigned(201, """{"shipmentNumber":"SH-1"}""", "header-1").CorrelationId.Should().Be("header-1");
    }

    [Theory]
    [InlineData(404, "TransportOrderNotFound")]
    [InlineData(409, "TransportOrderAlreadyPlanned")]
    [InlineData(409, "TransportOrderNotAccepted")]
    [InlineData(422, "InactiveCarrier")]
    [InlineData(422, "UnknownCarrier")]
    [InlineData(400, "ValidationFailed")]
    public void Assigned_ARefusal_GivesItsCodeAndTheTrailItWasRefusedOn_NeverItsMessage(int status, string code)
    {
        var answer = PlannerAnswers.Assigned(status, $$"""{"code":"{{code}}","detail":"Brouwer's Transport Order can't be assigned.","correlationId":"refused-1"}""", null);

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.Detail.Should().Be($"{code} ({status})");
        answer.FailureCode.Should().Be(code);
        answer.CorrelationId.Should().Be("refused-1");
        answer.ShipmentNumber.Should().BeNull();
    }

    [Theory]
    [InlineData(500, "")]
    [InlineData(401, "")]
    [InlineData(200, "not json")]
    [InlineData(409, """{"title":"no code"}""")]
    public void Assigned_AnythingElse_IsNotReceived(int status, string body)
    {
        var answer = PlannerAnswers.Assigned(status, body, null);

        answer.Outcome.Should().Be(LogOutcome.PlanningNotReceived);
        answer.Detail.Should().Contain(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Reassigned_AReassignedShipment_KeepsItsNumber()
    {
        var answer = PlannerAnswers.Reassigned(200, """{"carriedBy":{"type":"Carrier"},"transportOrderNumber":"TO-1","correlationId":"re-1"}""", null, "SH-0000007");

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.Reassigned, "re-1", "SH-0000007", null));
    }

    [Fact]
    public void Reassigned_AnUnchangedAssignment_IsRefused()
    {
        var answer = PlannerAnswers.Reassigned(409, """{"code":"AssignmentUnchanged","correlationId":"re-2"}""", null, "SH-1");

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.PlanningRefused, "re-2", "SH-1", "AssignmentUnchanged (409)", "AssignmentUnchanged"));
    }

    [Fact]
    public void Reassigned_A201_IsNotAReassignment()
    {
        PlannerAnswers.Reassigned(201, """{"correlationId":"x"}""", null, "SH-1").Outcome.Should().Be(LogOutcome.PlanningNotReceived);
    }

    [Fact]
    public void Days_TheOpenDays_GiveTheCapacityAndDemandOfEach()
    {
        var days = PlannerAnswers.Days("""
            [{"date":"2026-10-14","availableVehicles":2,"shifts":[],"load":{"capacityKg":12000.5,"capacityPalletPlaces":30,"demandKg":3000,"demandPalletPlaces":6,"over":false}},
             {"date":"2026-10-15","availableVehicles":0,"shifts":[],"load":{"capacityKg":0,"capacityPalletPlaces":0,"demandKg":0,"demandPalletPlaces":0,"over":false}}]
            """);

        days.Should().HaveCount(2);
        days![0].Should().BeEquivalentTo(new Domain.Planner.FleetDay(new DateOnly(2026, 10, 14), 12000.5m, 30, 3000, 6));
        days[1].CapacityKg.Should().Be(0);
    }

    [Fact]
    public void Days_TheDriversWhoWork_AreReadWithTheirShifts()
    {
        var days = PlannerAnswers.Days("""
            [{"date":"2026-10-19","availableVehicles":6,"shifts":[
                {"driverId":"DRV-0001","driverName":"Anouk de Vries","start":"06:00:00","end":"14:30:00","editedByHand":false},
                {"driverId":"DRV-0003","driverName":"Bram Visser","start":"08:00:00","end":"16:30:00","editedByHand":true}],
              "load":{"capacityKg":12000,"capacityPalletPlaces":30,"demandKg":0,"demandPalletPlaces":0,"over":false}}]
            """);

        days!.Single().Shifts.Should().Equal(
            new Domain.Planner.DriverShift("DRV-0001", new TimeOnly(6, 0), new TimeOnly(14, 30)),
            new Domain.Planner.DriverShift("DRV-0003", new TimeOnly(8, 0), new TimeOnly(16, 30)));
    }

    [Fact]
    public void Days_AShiftItCannotRead_IsLeftOut_AndTheDayStaysKnown()
    {
        var days = PlannerAnswers.Days("""
            [{"date":"2026-10-19","shifts":[{"driverId":"DRV-0001","start":"early","end":"14:30:00"},{"start":"06:00:00","end":"14:30:00"}],
              "load":{"capacityKg":1,"capacityPalletPlaces":1,"demandKg":0,"demandPalletPlaces":0}}]
            """);

        days!.Single().Shifts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""[{"date":"tomorrow","load":{}}]""")]
    [InlineData("""[{"date":"2026-10-14"}]""")]
    public void Days_AnythingElse_IsUnknown(string body)
    {
        PlannerAnswers.Days(body).Should().BeNull();
    }

    [Fact]
    public void Days_NoOpenDays_IsAnEmptyHorizon_NotUnknown()
    {
        PlannerAnswers.Days("[]").Should().BeEmpty();
    }

    [Fact]
    public void Released_ACarrierShipment_GivesItsCarrierBookingNumberAndTheTrailOfTheRelease()
    {
        var answer = PlannerAnswers.Released(200, """{"shipmentNumber":"SH-0000007","transportOrderNumber":"TO-1","status":"Released","carrierBookingNumber":"CB-0000042","correlationId":"rel-1"}""", "header-1");

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.Released, "rel-1", "SH-0000007", null, CarrierBookingNumber: "CB-0000042"));
    }

    [Fact]
    public void Released_AnOwnFleetShipment_HasNoCarrierBooking()
    {
        PlannerAnswers.Released(200, """{"shipmentNumber":"SH-1","correlationId":"rel-2"}""", null).CarrierBookingNumber.Should().BeNull();
    }

    [Theory]
    [InlineData(409, "ShipmentNotPlanned")]
    [InlineData(422, "NoRateCard")]
    [InlineData(422, "NoMatchingLane")]
    public void Released_ARefusal_GivesItsCode(int status, string code)
    {
        var answer = PlannerAnswers.Released(status, $$"""{"code":"{{code}}","detail":"x","correlationId":"refused-2"}""", null);

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.FailureCode.Should().Be(code);
        answer.Detail.Should().Be($"{code} ({status})");
    }

    [Fact]
    public void Cancelled_ACancelledCarrierBooking_GivesTheShipmentAndTheTrail()
    {
        var answer = PlannerAnswers.Cancelled(200, """{"exceptionNumber":"EXC-1","shipmentNumber":"SH-0000007","carrierBookingNumber":"CB-0000042","correlationId":"can-1"}""", null);

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.BookingCancelled, "can-1", "SH-0000007", null, CarrierBookingNumber: "CB-0000042"));
    }

    [Theory]
    [InlineData(409, "ShipmentAlreadyCollected")]
    [InlineData(409, "NoConfirmedCarrierBooking")]
    [InlineData(404, "ExceptionNotFound")]
    public void Cancelled_ARefusal_GivesItsCode(int status, string code)
    {
        PlannerAnswers.Cancelled(status, $$"""{"code":"{{code}}","correlationId":"c"}""", null).FailureCode.Should().Be(code);
    }

    [Fact]
    public void Resolved_AResolvedException_GivesTheShipmentAndTheTrail()
    {
        var answer = PlannerAnswers.Resolved(200, """{"exceptionNumber":"EXC-1","shipmentNumber":"SH-0000007","status":"Resolved","outcome":"Replanned","correlationId":"res-1"}""", null);

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.ExceptionResolved, "res-1", "SH-0000007", null));
    }

    [Fact]
    public void Resolved_NotOpen_IsRefusedWithItsCode()
    {
        var answer = PlannerAnswers.Resolved(409, """{"code":"ExceptionNotOpen","correlationId":"res-2"}""", null);

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.FailureCode.Should().Be("ExceptionNotOpen");
    }

    [Fact]
    public void OpenExceptions_TheList_GivesEachNumberShipmentAndType()
    {
        var list = PlannerAnswers.OpenExceptions("""
            [{"exceptionNumber":"EXC-1","shipmentNumber":"SH-1","type":"CollectionFailed","canCancelCarrierBooking":true},
             {"exceptionNumber":"EXC-2","shipmentNumber":"SH-2","type":"Reservations"}]
            """);

        list.Should().Equal(new Application.Ports.OpenExceptionSummary("EXC-1", "SH-1", "CollectionFailed"), new Application.Ports.OpenExceptionSummary("EXC-2", "SH-2", "Reservations"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"title":"not a list"}""")]
    [InlineData("""[{"exceptionNumber":"EXC-1"}]""")]
    public void OpenExceptions_SomethingElse_IsUnknown(string body)
    {
        PlannerAnswers.OpenExceptions(body).Should().BeNull();
    }

    [Fact]
    public void TripCreated_ADraftTrip_GivesItsNumber_AndNoTrailBecauseCreatingPublishesNothing()
    {
        var answer = PlannerAnswers.TripCreated(201, """{"number":"TR-0000012","warnings":[]}""", null);

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.TripBuilt, null, null, null, TripNumber: "TR-0000012"));
    }

    [Fact]
    public void TripCreated_ARefusal_GivesItsCode_NeverItsMessage()
    {
        var answer = PlannerAnswers.TripCreated(409, """{"code":"DrivingHoursExceeded","detail":"Anouk would drive 11 hours.","failures":[]}""", "refused-1");

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.Detail.Should().Be("DrivingHoursExceeded (409)");
        answer.FailureCode.Should().Be("DrivingHoursExceeded");
        answer.TripNumber.Should().BeNull();
    }

    [Fact]
    public void TripCreated_A200_IsNotACreation()
    {
        PlannerAnswers.TripCreated(200, """{"number":"TR-1"}""", null).Outcome.Should().Be(LogOutcome.PlanningNotReceived);
    }

    [Fact]
    public void TripDispatched_GivesTheTrailOfTheDispatch_AndTheTripThatWasSent()
    {
        var answer = PlannerAnswers.TripDispatched(200, """{"number":"TR-0000012","version":1,"correlationId":"disp-1"}""", "header", "TR-0000012");

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.TripDispatched, "disp-1", null, null, TripNumber: "TR-0000012"));
    }

    [Fact]
    public void TripDispatched_ARefusal_KeepsTheTripItWasAskedFor_AndGivesItsCode()
    {
        var answer = PlannerAnswers.TripDispatched(409, """{"code":"TripIncomplete","detail":"x"}""", "refused-2", "TR-0000012");

        answer.Outcome.Should().Be(LogOutcome.PlanningRefused);
        answer.Detail.Should().Be("TripIncomplete (409)");
        answer.CorrelationId.Should().Be("refused-2");
    }
}
