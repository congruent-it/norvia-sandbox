using Norvia.PartnerPortal.Web;

namespace Norvia.PartnerPortal.Web.UnitTests;

public sealed class EndpointTrackingTests
{
    [Fact]
    public void Keys_CarryTheFlowThePartyAndWhatTheRequestNames()
    {
        var keys = EndpointTracking.Keys("shipper-notification", "SHP-0001", "corr-1", ("transportOrderNumber", "TO-0000042"));

        keys.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["flow"] = "shipper-notification",
            ["partyId"] = "SHP-0001",
            ["correlationId"] = "corr-1",
            ["transportOrderNumber"] = "TO-0000042",
        });
    }

    [Fact]
    public void Keys_LeaveOutWhatTheRequestDoesNotName_NeverSendingThemEmpty()
    {
        var keys = EndpointTracking.Keys("carrier-booking", "CAR-0001", " ", ("shipmentNumber", null), ("carrierBookingNumber", ""));

        keys.Keys.Should().BeEquivalentTo("flow", "partyId");
    }

    [Fact]
    public void Outcome_WithoutAFailureCode_HasOnlyTheOutcome() =>
        EndpointTracking.Outcome("Succeeded", failureCode: null).Should().BeEquivalentTo(new Dictionary<string, object?> { ["outcome"] = "Succeeded" });

    [Fact]
    public void Outcome_WithAFailureCode_HasBoth() =>
        EndpointTracking.Outcome("Refused", "CredentialInvalid").Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["outcome"] = "Refused", ["failureCode"] = "CredentialInvalid" });
}
