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

    private static readonly ProofOfDeliveryCapturedV1 Captured = new(
        Guid.Parse("0198e7c0-0000-7000-8000-0000000000e2"), new DateTimeOffset(2026, 10, 13, 10, 12, 30, TimeSpan.FromHours(2)), "0198e7c0-0000-7000-8000-0000000000e3", "TR-0000012", 2, "DRV-0002",
        "SH-0000031", "0198e7c0-0000-7000-8000-0000000000e2", "J. Bakker", new DateTimeOffset(2026, 10, 13, 10, 12, 30, TimeSpan.FromHours(2)), new StopLocationV1("Utrecht", "3511 AB", "NL"),
        "Clean", null,
        [new ProofOfDeliveryFileV1("signature.png", "image/png", "0198e7c0-0000-7000-8000-0000000000e2/signature.png")]);

    [Fact]
    public void AProofOfDeliveryCaptured_ValidatesAgainstTheSchema_CarriesBlobReferencesAndNoFiles_AndLeavesOutARemarkTheDriverDidNotGive()
    {
        var clean = JsonNode.Parse(JsonSerializer.Serialize(Captured, Captured.GetType(), Web))!;
        var withReservations = Captured with { Condition = "Reservations", Remark = "Two cartons crushed" };
        var reservations = JsonNode.Parse(JsonSerializer.Serialize(withReservations, withReservations.GetType(), Web))!;

        Schemas.Validate("proof-of-delivery-captured.v1.schema.json", clean).IsValid.Should().BeTrue(Schemas.Errors(Schemas.Validate("proof-of-delivery-captured.v1.schema.json", clean)));
        Schemas.Validate("proof-of-delivery-captured.v1.schema.json", reservations).IsValid.Should().BeTrue();
        clean["eventType"]!.GetValue<string>().Should().Be("ProofOfDeliveryCaptured");
        clean["condition"]!.GetValue<string>().Should().Be("Clean");
        clean["files"]![0]!["blobReference"]!.GetValue<string>().Should().Be("0198e7c0-0000-7000-8000-0000000000e2/signature.png");
        clean.AsObject().ContainsKey("remark").Should().BeFalse("the schema allows no null remark");
        clean.AsObject().Select(property => property.Key).Should().NotContain(["topic", "sessionId", "applicationProperties"]);
    }

    [Fact]
    public void TheProofOfDeliveryCapturedSchema_NeedsARemarkWithReservations_AndOneToFiveFiles()
    {
        var file = Captured.Files[0];
        var cases = new (ProofOfDeliveryCapturedV1 Event, bool Valid)[]
        {
            (Captured with { Condition = "Reservations" }, false),
            (Captured with { Condition = "Reservations", Remark = "Crushed" }, true),
            (Captured with { Files = [] }, false),
            (Captured with { Files = [file, file, file, file, file] }, true),
            (Captured with { Files = [file, file, file, file, file, file] }, false),
            (Captured with { Condition = "Damaged" }, false),
        };

        foreach (var (candidate, valid) in cases)
        {
            var body = JsonNode.Parse(JsonSerializer.Serialize(candidate, candidate.GetType(), Web))!;
            Schemas.Validate("proof-of-delivery-captured.v1.schema.json", body).IsValid.Should().Be(valid, JsonSerializer.Serialize(candidate, Web));
        }
    }

    [Fact]
    public void TheProofOfDeliveryCapturedSamples_ValidateAgainstTheSchema()
    {
        Schemas.Samples("proof-of-delivery-captured").Should().HaveCountGreaterThanOrEqualTo(2);
        foreach (var sample in Schemas.Samples("proof-of-delivery-captured"))
        {
            var results = Schemas.Validate("proof-of-delivery-captured.v1.schema.json", Schemas.ReadSample(sample));
            results.IsValid.Should().BeTrue($"{sample}: {Schemas.Errors(results)}");
        }
    }

    [Fact]
    public void AProofOfDeliveryCaptured_GoesInTheSessionOfItsTrip_SharingItsCorrelationIdWithTheDelivered()
    {
        Captured.Topic.Should().Be("driver-app-events");
        Captured.SessionId.Should().Be("TR-0000012");
        Captured.ApplicationProperties.Should().Contain("eventType", "ProofOfDeliveryCaptured").And.Contain("correlationId", "0198e7c0-0000-7000-8000-0000000000e3");
        foreach (var sample in Schemas.Samples("stop-completed-reported").Where(sample => sample.Contains("delivered", StringComparison.Ordinal)))
        {
            Schemas.ReadSample(sample)["correlationId"]!.GetValue<string>().Should().Be(Captured.CorrelationId);
        }
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
