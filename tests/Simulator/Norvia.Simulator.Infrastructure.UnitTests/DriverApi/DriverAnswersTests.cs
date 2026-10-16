using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.DriverApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.DriverApi;

public sealed class DriverAnswersTests
{
    [Fact]
    public void Reported_AReportTheDriverAppTook_GivesTheTrailOfTheDriversAction()
    {
        var answer = DriverAnswers.Reported(200, """{"reportId":"3f2a4c9e-6c4a-4d0e-9c2a-0b1f6f0e8a11","correlationId":"drv-1","alreadyReported":false}""", "header-1");

        answer.Should().Be(new DriverCallAnswer(LogOutcome.DriverReported, "drv-1", null));
    }

    [Fact]
    public void Reported_AReportMadeBefore_SaysSo_AndFallsBackOnTheHeader()
    {
        var answer = DriverAnswers.Reported(200, """{"reportId":"3f2a4c9e-6c4a-4d0e-9c2a-0b1f6f0e8a11","alreadyReported":true}""", "header-1");

        answer.Should().Be(new DriverCallAnswer(LogOutcome.DriverReported, "header-1", "Reported before"));
    }

    [Theory]
    [InlineData(404, "TripNotFound")]
    [InlineData(409, "ReportNotAvailable")]
    [InlineData(400, "ReportInvalid")]
    public void Reported_ARefusal_GivesItsCode_NeverItsMessage(int status, string code)
    {
        var answer = DriverAnswers.Reported(status, $$"""{"title":"The Stop can't be reported on: Remark text","code":"{{code}}"}""", null);

        answer.Should().Be(new DriverCallAnswer(LogOutcome.DriverRefused, null, $"{code} ({status})"));
    }

    [Theory]
    [InlineData(500, "")]
    [InlineData(401, "")]
    [InlineData(200, "not json")]
    [InlineData(409, """{"title":"no code"}""")]
    public void Reported_AnythingElse_IsNotReceived(int status, string body)
    {
        var answer = DriverAnswers.Reported(status, body, null);

        answer.Outcome.Should().Be(LogOutcome.DriverNotReceived);
        answer.Detail.Should().Contain(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Trips_AreReadWithTheirStatusAndTheReportsThatWereNotRecorded()
    {
        var trips = DriverAnswers.Trips("""
            [
              {"tripNumber":"TR-0000001","version":2,"status":"Dispatched","reports":[]},
              {"tripNumber":"TR-0000002","version":1,"status":"In progress","reports":[{"state":"Recorded"},{"state":"Refused"},{"state":"NotRecorded"},{"state":"Sending"}]},
              {"tripNumber":"TR-0000003","version":1,"status":"Completed"}
            ]
            """);

        trips.Should().Equal(
            new DriverTripSnapshot("TR-0000001", 2, DriverTripStatus.Dispatched, 0),
            new DriverTripSnapshot("TR-0000002", 1, DriverTripStatus.InProgress, 2),
            new DriverTripSnapshot("TR-0000003", 1, DriverTripStatus.Completed, 0));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"tripNumber":"TR-1"}""")]
    [InlineData("""[{"version":1,"status":"Completed"}]""")]
    [InlineData("""[{"tripNumber":"TR-1","status":"Completed"}]""")]
    [InlineData("""[{"tripNumber":"TR-1","version":1}]""")]
    public void Trips_AnythingThatIsNotAListOfTrips_IsNotRead(string body)
    {
        DriverAnswers.Trips(body).Should().BeNull();
    }
}
