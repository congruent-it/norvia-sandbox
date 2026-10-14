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

        answer.Should().Be(new Application.Ports.PlannerAnswer(LogOutcome.PlanningRefused, "re-2", "SH-1", "AssignmentUnchanged (409)"));
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
        days![0].Should().Be(new Domain.Planner.FleetDay(new DateOnly(2026, 10, 14), 12000.5m, 30, 3000, 6));
        days[1].CapacityKg.Should().Be(0);
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
}
