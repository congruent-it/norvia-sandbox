using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class CarrierAnswerTrackingTests
{
    [Fact]
    public void KeysOf_AreTheOnesThePortalKnows_LeavingOutACorrelationIdNorviaNeverGave()
    {
        CarrierAnswerTracking.KeysOf(Samples.VanDijkTransport, "CB-1", "SH-1", "m-1").Should()
            .Contain("flow", "carrier-answer").And.Contain("partyId", "CAR-0001").And.Contain("carrierBookingNumber", "CB-1")
            .And.Contain("shipmentNumber", "SH-1").And.Contain("correlationId", "m-1");
        CarrierAnswerTracking.KeysOf(Samples.VanDijkTransport, "CB-1", "SH-1", null).Should().NotContainKey("correlationId");
    }

    [Theory]
    [InlineData(CarrierAnswerResult.Recorded, "Succeeded", null)]
    [InlineData(CarrierAnswerResult.BookingNotOpen, "Refused", "BookingNotOpen")]
    [InlineData(CarrierAnswerResult.ReferenceInUse, "Refused", "ReferenceInUse")]
    [InlineData(CarrierAnswerResult.BookingNotFound, "Refused", "BookingNotFound")]
    [InlineData(CarrierAnswerResult.NotAllowed, "Refused", "CarrierNotAllowed")]
    [InlineData(CarrierAnswerResult.Invalid, "Refused", "CarrierAnswerUnreadable")]
    [InlineData(CarrierAnswerResult.Refused, "Refused", null)]
    [InlineData(CarrierAnswerResult.NotReceived, "Failed", "AnswerNotReceived")]
    public void OutcomeOf_EveryResult_HasItsOutcome_AndAFailureCodeOnlyWhenItHasOne(CarrierAnswerResult result, string outcome, string? failureCode)
    {
        var keys = CarrierAnswerTracking.OutcomeOf(result);

        keys.Should().Contain("outcome", outcome);
        if (failureCode is null)
        {
            keys.Should().NotContainKey("failureCode");
        }
        else
        {
            keys.Should().Contain("failureCode", failureCode);
        }
    }
}
