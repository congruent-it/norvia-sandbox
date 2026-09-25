using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>The sample messages in contracts/partner-api/samples keep to the Norvia Partner API's schemas.</summary>
public sealed class PartnerApiContractTests
{
    /// <summary>The one sample that breaks the booking schema on purpose, to show what the map does with values it can't read.</summary>
    private const string UnreadableSample = "unreadable";

    public static TheoryData<string> Bookings => new(PartnerApiSamples.BookingNames().Where(name => name != UnreadableSample));

    public static TheoryData<string> Responses => new(PartnerApiSamples.ResponseNames());

    [Theory]
    [MemberData(nameof(Bookings))]
    public void Validate_SampleBooking_KeepsToTheBookingSchema(string sample)
    {
        var results = Schemas.PartnerBooking.Validate(PartnerApiSamples.Booking(sample));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Fact]
    public void Validate_UnreadableSampleBooking_BreaksTheBookingSchema()
    {
        Schemas.PartnerBooking.Validate(PartnerApiSamples.Booking(UnreadableSample)).IsValid.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Responses))]
    public void Validate_SampleResponse_KeepsToTheResponseSchema(string sample)
    {
        var results = Schemas.PartnerBookingResponse.Validate(PartnerApiSamples.Response(sample));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Fact]
    public void Validate_AcceptedWithoutOrderNumber_BreaksTheResponseSchema()
    {
        var response = new JsonObject { ["result"] = "ACCEPTED", ["messageId"] = "7f3c1a52-9d4e-4b8a-a1f0-2e6d8c5b9a31" };

        Schemas.PartnerBookingResponse.Validate(response).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_RejectedWithoutErrors_BreaksTheResponseSchema()
    {
        var response = new JsonObject
        {
            ["result"] = "REJECTED",
            ["messageId"] = "5a9e2b14-6c3d-47f1-8b20-d4e7f1a0c963",
            ["errors"] = new JsonArray(),
        };

        Schemas.PartnerBookingResponse.Validate(response).IsValid.Should().BeFalse();
    }
}
