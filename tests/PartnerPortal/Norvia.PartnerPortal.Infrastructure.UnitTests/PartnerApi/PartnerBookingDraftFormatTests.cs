using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

/// <summary>The portal reads a Booking draft (contracts/partner-api/booking-draft.v1.schema.json) into the booking form's model.</summary>
public sealed class PartnerBookingDraftFormatTests
{
    private const string Good = """
        {
          "booking": {
            "bookingRef": "SH-2030-0142",
            "pickupStreet": "Havenstraat 12", "pickupPostalCode": "3011 AB", "pickupCity": "Rotterdam", "pickupCountry": "Netherlands",
            "pickupFrom": "14-11-2030 08:00", "pickupUntil": "14-11-2030 12:00",
            "receiverName": "Mueller GmbH", "receiverContact": "Anna Mueller", "receiverPhone": "+49 221 555 0100",
            "receiverStreet": "Lindenallee 5", "receiverPostalCode": "50667", "receiverCity": "Koeln", "receiverCountry": "Germany",
            "remarks": "Call first",
            "items": [ { "packaging": "PX", "count": 2, "weight": "850 kg", "dimensions": "120x80x150 cm" } ]
          },
          "quotes": {},
          "missing": [ { "field": "receiverEmail", "reason": "The text gives no email." } ]
        }
        """;

    [Fact]
    public void Read_ADraft_FillsTheBookingAndKeepsTheFlags()
    {
        var answer = PartnerBookingDraftFormat.Read(200, Good, "corr-1");

        answer.Outcome.Should().Be(BookingDraftOutcome.Drafted);
        answer.MessageId.Should().Be("corr-1");
        var booking = answer.Booking!;
        booking.ShipperReference.Should().Be("SH-2030-0142");
        booking.CollectionAddress.Should().Be(new BookingAddress("Havenstraat 12", "3011 AB", "Rotterdam", "Netherlands"));
        booking.CollectionWindow.Should().Be(new LocalWindow(new DateTime(2030, 11, 14, 8, 0, 0), new DateTime(2030, 11, 14, 12, 0, 0)));
        booking.Consignee.Should().Be(new BookingConsignee("Mueller GmbH", "Anna Mueller", "+49 221 555 0100"));
        booking.DeliveryWindow.Should().Be(new LocalWindow());
        booking.Instructions.Should().Be("Call first");
        booking.GoodsLines.Should().Equal(new BookingGoodsLine(PackageType.Pallet, 2, 850m, new PackageDimensions(120, 80, 150)));
        answer.Flags.Should().Equal(new FlaggedField("receiverEmail", "The text gives no email."));
    }

    [Fact]
    public void Read_ADraft_KeepsTheQuotesAndWhatTheAgentIgnored()
    {
        var answer = PartnerBookingDraftFormat.Read(
            200,
            """
            {"booking": {"pickupCity": "Utrecht"}, "quotes": {"pickupCity": "ons magazijn in Utrecht", "bookingRef": "  "}, "missing": [], "ignored": ["Groet, Jan", " ", "Vertrouwelijk"]}
            """,
            null);

        answer.Quotes.Should().Equal(new Dictionary<string, string> { ["pickupCity"] = "ons magazijn in Utrecht" });
        answer.Ignored.Should().Equal("Groet, Jan", "Vertrouwelijk");
    }

    [Fact]
    public void Read_AnAmbiguousField_IsFlaggedToCheck_AndAMissingOneIsNot()
    {
        var answer = PartnerBookingDraftFormat.Read(
            200,
            """
            {"booking": {}, "quotes": {}, "missing": [
              {"field": "items.0.weight", "reason": "850 kg and 900 kg.", "kind": "ambiguous"},
              {"field": "receiverEmail", "reason": "No email.", "kind": "missing"},
              {"field": "bookingRef", "reason": "No reference."}]}
            """,
            null);

        answer.Flags.Should().Equal(
            new FlaggedField("items.0.weight", "850 kg and 900 kg.", CheckThis: true),
            new FlaggedField("receiverEmail", "No email."),
            new FlaggedField("bookingRef", "No reference."));
    }

    [Fact]
    public void Read_AValueTheFormCannotRead_IsFlaggedToCheck()
    {
        var answer = PartnerBookingDraftFormat.Read(200, """{"booking": {"items": [{"weight": "heavy"}]}, "quotes": {}, "missing": []}""", null);

        answer.Flags.Should().Equal(new FlaggedField("items.0.weight", "The weight 'heavy' could not be read.", CheckThis: true));
    }

    [Fact]
    public void Read_ADraftWithoutQuotesOrIgnored_HasNone()
    {
        var answer = PartnerBookingDraftFormat.Read(200, """{"booking": {}, "missing": []}""", null);

        answer.Quotes.Should().BeEmpty();
        answer.Ignored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2,5 t", 2500)]
    [InlineData("850 lb", 385.554)]
    [InlineData("12.5 kg", 12.5)]
    public void Read_AWeightInAnotherUnit_IsConvertedToKilograms(string weight, double kilograms)
    {
        var answer = PartnerBookingDraftFormat.Read(200, $$"""{"booking": {"items": [{"weight": "{{weight}}"}]}, "quotes": {}, "missing": []}""", null);

        answer.Booking!.GoodsLines[0].WeightKg.Should().Be((decimal)kilograms);
    }

    [Fact]
    public void Read_Dimensions_AreConvertedToCentimetres()
    {
        var answer = PartnerBookingDraftFormat.Read(200, """{"booking": {"items": [{"dimensions": "1200 x 800 x 1500 mm"}]}, "quotes": {}, "missing": []}""", null);

        answer.Booking!.GoodsLines[0].DimensionsCm.Should().Be(new PackageDimensions(120, 80, 150));
    }

    [Fact]
    public void Read_AValueTheFormCannotHold_IsLeftEmptyAndFlagged()
    {
        var answer = PartnerBookingDraftFormat.Read(
            200, """{"booking": {"pickupFrom": "tomorrow", "items": [{"weight": "heavy", "dimensions": "big"}]}, "quotes": {}, "missing": []}""", null);

        answer.Booking!.CollectionWindow.From.Should().BeNull();
        answer.Booking.GoodsLines[0].WeightKg.Should().BeNull();
        answer.Booking.GoodsLines[0].DimensionsCm.Should().BeNull();
        answer.Flags.Select(flag => flag.Field).Should().BeEquivalentTo(["pickupFrom", "items.0.weight", "items.0.dimensions"]);
    }

    [Fact]
    public void Read_ADraft_SendsAsABookingTheSchemaAccepts()
    {
        var answer = PartnerBookingDraftFormat.Read(200, Good, null);

        Contracts.BookingSchemaErrors(PartnerBookingFormat.ToJson(answer.Booking!)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(422, """{"failureCode": "MultipleBookingsInText", "messageId": "m-1"}""", BookingDraftOutcome.MultipleBookings)]
    [InlineData(413, """{"failureCode": "InputTooLarge"}""", BookingDraftOutcome.TooLarge)]
    [InlineData(503, """{"failureCode": "AgentsDisabled"}""", BookingDraftOutcome.Unavailable)]
    [InlineData(503, """{"failureCode": "AgentUnavailable"}""", BookingDraftOutcome.Unavailable)]
    [InlineData(403, """{"text": "no"}""", BookingDraftOutcome.Unavailable)]
    [InlineData(401, """{"statusCode": 401}""", BookingDraftOutcome.Unavailable)]
    [InlineData(429, "not json", BookingDraftOutcome.Unavailable)]
    [InlineData(200, "not json", BookingDraftOutcome.Unavailable)]
    public void Read_ARefusalOrAFailure_IsAnOutcomeWithNoBooking(int status, string body, BookingDraftOutcome outcome)
    {
        var answer = PartnerBookingDraftFormat.Read(status, body, null);

        answer.Outcome.Should().Be(outcome);
        answer.Booking.Should().BeNull();
    }

    [Fact]
    public void Read_ARefusal_KeepsTheCorrelationIdFromTheBody()
    {
        PartnerBookingDraftFormat.Read(422, """{"failureCode": "MultipleBookingsInText", "messageId": "m-1"}""", null).MessageId.Should().Be("m-1");
    }
}
