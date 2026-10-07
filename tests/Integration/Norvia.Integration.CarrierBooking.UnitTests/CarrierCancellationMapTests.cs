using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public class CarrierCancellationMapTests
{
    private const string Folder = "PartnerApi/samples/carrier-cancellations";

    private static JsonObject Event(string sample, Action<JsonObject>? change = null)
    {
        var node = JsonNode.Parse(Contracts.Read($"{Folder}/{sample}"))!.AsObject();
        change?.Invoke(node);
        return node;
    }

    private static JsonObject Cancelled(Action<JsonObject>? change = null) => Event("cancelled.carrier-booking-cancelled.json", change);

    [Fact]
    public void ToCarrierCancellation_ACancelledBooking_GivesKindCancelledWithReferenceAndReason()
    {
        var cancellation = CarrierCancellationMap.ToCarrierCancellation(Cancelled().ToJsonString());

        cancellation!["kind"]!.GetValue<string>().Should().Be("CANCELLED");
        cancellation["carrierBookingReference"]!.GetValue<string>().Should().Be("KESSLER-88213");
        cancellation["reasonText"]!.GetValue<string>().Should().NotBeEmpty();
    }

    [Fact]
    public void ToCarrierCancellation_AWithdrawnBooking_GivesKindWithdrawnWithoutReferenceOrReason()
    {
        var cancellation = CarrierCancellationMap.ToCarrierCancellation(Event("withdrawn.carrier-booking-withdrawn.json").ToJsonString());

        cancellation!["kind"]!.GetValue<string>().Should().Be("WITHDRAWN");
        cancellation.ContainsKey("carrierBookingReference").Should().BeFalse();
        cancellation.ContainsKey("reasonText").Should().BeFalse();
    }

    [Fact]
    public void ToCarrierCancellation_AWithdrawnEventWithAReference_LeavesTheReferenceOut()
    {
        var withdrawn = Event("withdrawn.carrier-booking-withdrawn.json", e => e["carrierBookingReference"] = "X-1");

        CarrierCancellationMap.ToCarrierCancellation(withdrawn.ToJsonString())!.ContainsKey("carrierBookingReference").Should().BeFalse();
    }

    [Theory]
    [InlineData("carrierBookingNumber")]
    [InlineData("shipmentNumber")]
    [InlineData("carrierBookingReference")]
    [InlineData("reasonText")]
    public void ToCarrierCancellation_ACancelledEventWithoutARequiredValue_GivesNothing(string name) =>
        CarrierCancellationMap.ToCarrierCancellation(Cancelled(e => e[name] = null).ToJsonString()).Should().BeNull();

    [Theory]
    [InlineData("carrierBookingNumber")]
    [InlineData("shipmentNumber")]
    public void ToCarrierCancellation_AWithdrawnEventWithoutARequiredValue_GivesNothing(string name) =>
        CarrierCancellationMap.ToCarrierCancellation(Event("withdrawn.carrier-booking-withdrawn.json", e => e[name] = null).ToJsonString()).Should().BeNull();

    [Fact]
    public void ToCarrierCancellation_AnUnknownEventType_GivesNothing() =>
        CarrierCancellationMap.ToCarrierCancellation(Cancelled(e => e["eventType"] = "CarrierBookingRejected").ToJsonString()).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void ToCarrierCancellation_TextThatIsNoEvent_GivesNothing(string text) =>
        CarrierCancellationMap.ToCarrierCancellation(text).Should().BeNull();
}
