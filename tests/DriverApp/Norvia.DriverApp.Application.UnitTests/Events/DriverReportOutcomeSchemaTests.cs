using System.Text.Json.Nodes;

namespace Norvia.DriverApp.Application.UnitTests.Events;

/// <summary>The outcome report the Driver reports Logic App sends is the Canonical Model's <c>driver-report-outcome</c>.</summary>
public sealed class DriverReportOutcomeSchemaTests
{
    private const string Schema = "driver-report-outcome.v1.schema.json";

    [Fact]
    public void TheSamples_ValidateAgainstTheSchema()
    {
        var samples = Schemas.Samples("driver-report-outcome").ToList();

        samples.Should().HaveCount(3, "Recorded, Refused and Not recorded");
        foreach (var sample in samples)
        {
            var results = Schemas.Validate(Schema, Schemas.ReadSample(sample));
            results.IsValid.Should().BeTrue($"{sample}: {Schemas.Errors(results)}");
        }
    }

    [Theory]
    [InlineData("""{"driverAppEventId":"0198e7c0-0000-7000-8000-0000000000c1","tripNumber":"TR-0000012","correlationId":"c","outcome":"Refused","message":"No."}""")]
    [InlineData("""{"driverAppEventId":"0198e7c0-0000-7000-8000-0000000000c1","tripNumber":"TR-0000012","correlationId":"c","outcome":"Refused","failureCode":"NotTripDay"}""")]
    [InlineData("""{"driverAppEventId":"0198e7c0-0000-7000-8000-0000000000c1","tripNumber":"TR-0000012","correlationId":"c","outcome":"NotRecorded"}""")]
    [InlineData("""{"driverAppEventId":"0198e7c0-0000-7000-8000-0000000000c1","tripNumber":"TR-0000012","correlationId":"c","outcome":"Recorded","message":"Done."}""")]
    [InlineData("""{"driverAppEventId":"not-a-guid","tripNumber":"TR-0000012","correlationId":"c","outcome":"Recorded"}""")]
    [InlineData("""{"driverAppEventId":"0198e7c0-0000-7000-8000-0000000000c1","tripNumber":"TR-0000012","correlationId":"c","outcome":"Lost"}""")]
    public void AnOutcomeWithoutWhatItNeeds_OrWithWhatItMustNotHave_IsInvalid(string json)
    {
        Schemas.Validate(Schema, JsonNode.Parse(json)!).IsValid.Should().BeFalse();
    }
}
