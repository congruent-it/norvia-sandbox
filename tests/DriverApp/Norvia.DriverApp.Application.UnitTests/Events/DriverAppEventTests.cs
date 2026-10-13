using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Application.UnitTests.Events;

/// <summary>The Driver app's event follows the Canonical Model, and its message carries the tracking contract's keys (coding guidelines §7).</summary>
public sealed class DriverAppEventTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly TripStartReportedV1 Event = new(
        Guid.Parse("0198e7c0-0000-7000-8000-0000000000c1"), new DateTimeOffset(2026, 10, 13, 8, 5, 12, TimeSpan.FromHours(2)), "0198e7c0-0000-7000-8000-0000000000c2", "TR-0000012", 2, "DRV-0002");

    [Fact]
    public void TheBody_IsATripStartReported_ThatValidatesAgainstTheSchema_WithoutTheMessageOnlyParts()
    {
        var body = JsonNode.Parse(JsonSerializer.Serialize(Event, Event.GetType(), Web))!;

        var results = Schemas.Validate("trip-start-reported.v1.schema.json", body);

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
        body["eventType"]!.GetValue<string>().Should().Be("TripStartReported");
        body["driverId"]!.GetValue<string>().Should().Be("DRV-0002");
        body.AsObject().Select(property => property.Key).Should().NotContain(["topic", "sessionId", "applicationProperties"]);
    }

    [Fact]
    public void TheSample_ValidatesAgainstTheSchema()
    {
        foreach (var sample in Schemas.Samples("trip-start-reported"))
        {
            var results = Schemas.Validate("trip-start-reported.v1.schema.json", Schemas.ReadSample(sample));
            results.IsValid.Should().BeTrue($"{sample}: {Schemas.Errors(results)}");
        }
    }

    private static readonly StopCompletedReportedV1 StopEvent = new(
        Guid.Parse("0198e7c0-0000-7000-8000-0000000000d3"), new DateTimeOffset(2026, 10, 13, 11, 20, 45, TimeSpan.FromHours(2)), "0198e7c0-0000-7000-8000-0000000000d4", "TR-0000012", 2, "DRV-0002",
        "SH-0000032", "Delivery", "DeliveryFailed", new StopLocationV1("Utrecht", "3511 AB", "NL"), "Dock closed, nobody at the warehouse");

    [Fact]
    public void AStopEvent_ValidatesAgainstTheSchema_AndLeavesOutARemarkTheDriverDidNotGive()
    {
        var withRemark = JsonNode.Parse(JsonSerializer.Serialize(StopEvent, StopEvent.GetType(), Web))!;
        var collected = StopEvent with { Stop = "Collection", TrackingEventType = "Collected", Remark = null };
        var withoutRemark = JsonNode.Parse(JsonSerializer.Serialize(collected, collected.GetType(), Web))!;

        Schemas.Validate("stop-completed-reported.v1.schema.json", withRemark).IsValid.Should().BeTrue(Schemas.Errors(Schemas.Validate("stop-completed-reported.v1.schema.json", withRemark)));
        Schemas.Validate("stop-completed-reported.v1.schema.json", withoutRemark).IsValid.Should().BeTrue(Schemas.Errors(Schemas.Validate("stop-completed-reported.v1.schema.json", withoutRemark)));
        withRemark["eventType"]!.GetValue<string>().Should().Be("StopCompletedReported");
        withRemark["trackingEventType"]!.GetValue<string>().Should().Be("DeliveryFailed");
        withRemark["location"]!["countryCode"]!.GetValue<string>().Should().Be("NL");
        withoutRemark.AsObject().ContainsKey("remark").Should().BeFalse("the schema allows no null remark");
    }

    [Fact]
    public void TheStopSamples_ValidateAgainstTheSchema()
    {
        Schemas.Samples("stop-completed-reported").Should().HaveCountGreaterThanOrEqualTo(3);
        foreach (var sample in Schemas.Samples("stop-completed-reported"))
        {
            var results = Schemas.Validate("stop-completed-reported.v1.schema.json", Schemas.ReadSample(sample));
            results.IsValid.Should().BeTrue($"{sample}: {Schemas.Errors(results)}");
        }
    }

    [Theory]
    [InlineData("CollectionFailed", "Collection", false, false)]
    [InlineData("CollectionFailed", "Collection", true, true)]
    [InlineData("DeliveryFailed", "Delivery", false, false)]
    [InlineData("Damaged", "Collection", false, false)]
    [InlineData("Damaged", "Delivery", true, true)]
    [InlineData("Collected", "Collection", false, true)]
    [InlineData("Delivered", "Delivery", false, true)]
    [InlineData("Collected", "Delivery", false, false)]
    [InlineData("Delivered", "Collection", false, false)]
    [InlineData("Lost", "Delivery", true, false)]
    public void TheSchema_NeedsARemarkForAFailureOrDamage_AndTheTrackingEventOfThatKindOfStop(string type, string stop, bool remarkGiven, bool valid)
    {
        var body = JsonNode.Parse(JsonSerializer.Serialize(StopEvent with { TrackingEventType = type, Stop = stop, Remark = remarkGiven ? "A remark" : null }, Web))!;

        Schemas.Validate("stop-completed-reported.v1.schema.json", body).IsValid.Should().Be(valid);
    }

    [Fact]
    public void AStopEventsMessage_GoesInTheSessionOfItsTrip_AndCarriesTheKeysItKnows()
    {
        StopEvent.Topic.Should().Be("driver-app-events");
        StopEvent.SessionId.Should().Be("TR-0000012");
        StopEvent.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "StopCompletedReported",
            ["eventVersion"] = 1,
            ["flow"] = "trip-execution",
            ["partyId"] = "DRV-0002",
            ["tripNumber"] = "TR-0000012",
            ["sourceMessageId"] = "0198e7c0-0000-7000-8000-0000000000d3",
        });
    }

    [Fact]
    public void TheMessage_GoesToTheDriverAppEventsTopic_InTheSessionOfItsTrip()
    {
        Event.Topic.Should().Be("driver-app-events");
        Event.SessionId.Should().Be("TR-0000012");
    }

    [Fact]
    public void TheApplicationProperties_CarryTheKeysTheEventKnows()
    {
        Event.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "TripStartReported",
            ["eventVersion"] = 1,
            ["correlationId"] = "0198e7c0-0000-7000-8000-0000000000c2",
            ["flow"] = "trip-execution",
            ["partyId"] = "DRV-0002",
            ["tripNumber"] = "TR-0000012",
            ["sourceMessageId"] = "0198e7c0-0000-7000-8000-0000000000c1",
        });
    }
}

internal static class Schemas
{
    private static readonly Dictionary<string, JsonSchema> Loaded = [];

    public static IEnumerable<string> Samples(string folder) =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(name => name.StartsWith($"CanonicalModel/samples/{folder}/", StringComparison.Ordinal));

    public static JsonNode ReadSample(string resource) => JsonNode.Parse(Read(resource))!;

    public static EvaluationResults Validate(string schema, JsonNode body)
    {
        if (!Loaded.TryGetValue(schema, out var loaded))
        {
            loaded = JsonSchema.FromText(Read($"CanonicalModel/{schema}"));
            Loaded[schema] = loaded;
        }

        return loaded.Evaluate(JsonSerializer.SerializeToElement(body), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
    }

    public static string Errors(EvaluationResults results) =>
        string.Join("; ", results.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []);

    private static string Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
