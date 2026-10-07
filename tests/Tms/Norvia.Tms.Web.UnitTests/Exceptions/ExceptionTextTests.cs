using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Web.Exceptions;

namespace Norvia.Tms.Web.UnitTests.Exceptions;

public sealed class ExceptionTextTests
{
    [Theory]
    [InlineData(ExceptionType.DeliveryFailed, "Delivery failed")]
    [InlineData(ExceptionType.CollectionFailed, "Collection failed")]
    [InlineData(ExceptionType.Damaged, "Damaged")]
    [InlineData(ExceptionType.Reservations, "Reservations")]
    [InlineData(ExceptionType.LateDelivery, "Late delivery")]
    public void Type_IsWordedAsTheGlossaryDoes(ExceptionType type, string expected) => ExceptionText.Type(type).Should().Be(expected);

    [Theory]
    [InlineData(ExceptionOutcome.NoActionNeeded, "No action needed")]
    [InlineData(ExceptionOutcome.Redelivered, "Redelivered")]
    [InlineData(ExceptionOutcome.Replanned, "Replanned")]
    [InlineData(ExceptionOutcome.ReturnedToShipper, "Returned to Shipper")]
    [InlineData(ExceptionOutcome.ClaimHandledOutsideTms, "Claim handled outside the TMS")]
    public void Outcome_IsWordedAsAPlannerReadsIt(ExceptionOutcome outcome, string expected) => ExceptionText.Outcome(outcome).Should().Be(expected);

    [Theory]
    [InlineData(ExceptionCauseKind.TrackingEvent, "Tracking event")]
    [InlineData(ExceptionCauseKind.ProofOfDelivery, "Proof of delivery")]
    [InlineData(ExceptionCauseKind.WindowPassed, "Delivery window passed")]
    public void Cause_IsWordedAsAPlannerReadsIt(ExceptionCauseKind kind, string expected) => ExceptionText.Cause(kind).Should().Be(expected);

    [Theory]
    [InlineData(ShipperNotificationStatus.Requested, "Requested")]
    [InlineData(ShipperNotificationStatus.Sent, "Sent")]
    [InlineData(ShipperNotificationStatus.NotDelivered, "Not delivered")]
    public void Notification_IsWordedAsAPlannerReadsIt(ShipperNotificationStatus status, string expected) => ExceptionText.Notification(status).Should().Be(expected);
}
