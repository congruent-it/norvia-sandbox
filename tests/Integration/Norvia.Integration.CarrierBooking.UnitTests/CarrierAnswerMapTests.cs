using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public sealed class CarrierAnswerMapTests
{
    private const string CarrierId = "CAR-0001";

    private static MappedCarrierAnswer Map(string json) => CarrierAnswerMap.ToCarrierBookingAnswer(json, CarrierId);

    [Fact]
    public void ToCarrierBookingAnswer_APayloadNamingAnotherCarrier_StillAnswersAsTheCarrierOfTheToken()
    {
        var mapped = Map("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "R-1", "carrierId": "CAR-0002" }""");

        mapped.TmsRequest!["carrierId"]!.GetValue<string>().Should().Be(CarrierId);
        mapped.TmsRequest.ToJsonString().Should().NotContain("CAR-0002");
    }

    [Theory]
    [InlineData("NO_CAPACITY", "NoCapacity")]
    [InlineData("LANE_NOT_SERVED", "LaneNotServed")]
    [InlineData("WINDOW_NOT_FEASIBLE", "WindowNotFeasible")]
    [InlineData("GOODS_NOT_ACCEPTED", "GoodsNotAccepted")]
    public void ToCarrierBookingAnswer_EveryReasonCode_MapsToTheCanonicalOne(string carrierReason, string canonicalReason)
    {
        var mapped = Map($$"""{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "{{carrierReason}}" }""");

        mapped.TmsRequest!["reasonCode"]!.GetValue<string>().Should().Be(canonicalReason);
        mapped.TmsRequest.ContainsKey("reasonText").Should().BeFalse();
    }

    [Fact]
    public void ToCarrierBookingAnswer_APlannedCollectionInUtc_StaysTheSameInstant()
    {
        var mapped = Map("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "R-1", "plannedCollection": "2030-10-02T06:30:00Z" }""");

        mapped.TmsRequest!["plannedCollectionTime"]!.GetValue<string>().Should().Be("2030-10-02T06:30:00+00:00");
    }

    [Theory]
    [InlineData("""{ "decision": "CONFIRMED", "bookingRef": "R-1" }""", "carrierBookingNumber")]
    [InlineData("""{ "carrierBookingNumber": "42", "decision": "CONFIRMED", "bookingRef": "R-1" }""", "carrierBookingNumber")]
    [InlineData("not json", "carrierBookingNumber")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "MAYBE" }""", "CONFIRMED or DECLINED")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042" }""", "CONFIRMED or DECLINED")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED" }""", "bookingRef")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "  " }""", "bookingRef")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "R-1", "plannedCollection": "2030-10-02T08:30:00" }""", "UTC offset")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "R-1", "plannedCollection": "tomorrow" }""", "UTC offset")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED" }""", "declineReason")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "TOO_FAR" }""", "NO_CAPACITY")]
    [InlineData("""{ "carrierBookingNumber": "CB-0000042", "decision": "DECLINED", "declineReason": "OTHER" }""", "declineText")]
    public void ToCarrierBookingAnswer_AnAnswerThatCantBeUsed_SaysWhyAndHasNothingForTheTms(string answer, string problemMentions)
    {
        var mapped = Map(answer);

        mapped.TmsRequest.Should().BeNull();
        mapped.Problem.Should().Contain(problemMentions);
    }

    [Fact]
    public void ToCarrierBookingAnswer_AnUnusableAnswerThatNamedTheBooking_KeepsItsNumber()
    {
        Map("""{ "carrierBookingNumber": "CB-0000042", "decision": "MAYBE" }""").CarrierBookingNumber.Should().Be("CB-0000042");
    }

    [Fact]
    public void ToCarrierBookingAnswer_WithoutACarrier_Throws()
    {
        var map = () => CarrierAnswerMap.ToCarrierBookingAnswer("{}", " ");

        map.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MappedAnswer_RoundTripsThroughTheWorkflowsJson()
    {
        var mapped = Map("""{ "carrierBookingNumber": "CB-0000042", "decision": "CONFIRMED", "bookingRef": "R-1" }""");
        var invalid = Map("""{ "carrierBookingNumber": "CB-0000042", "decision": "MAYBE" }""");

        MappedCarrierAnswer.FromJson(mapped.ToJsonString()).Should().BeEquivalentTo(mapped, options => options.ComparingByMembers<MappedCarrierAnswer>().Excluding(a => a.TmsRequest));
        JsonNode.DeepEquals(MappedCarrierAnswer.FromJson(mapped.ToJsonString()).TmsRequest, mapped.TmsRequest).Should().BeTrue();
        MappedCarrierAnswer.FromJson(invalid.ToJsonString()).Should().Be(invalid);
    }

    [Fact]
    public void FromJson_SomethingThatIsNoObject_Throws()
    {
        var read = () => MappedCarrierAnswer.FromJson("[]");

        read.Should().Throw<ArgumentException>();
    }
}
