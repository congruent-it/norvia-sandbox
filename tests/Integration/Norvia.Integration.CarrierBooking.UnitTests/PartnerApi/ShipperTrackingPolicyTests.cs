using System.Xml.Linq;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// API Management's four operations of the Shipper tracking API (P8): list, get, find and the download of a Proof of
/// delivery file. Each checks the same things in the same order, then queries the TMS as API Management's identity with the
/// Shipper ID and a correlation ID, waits a short time without retrying, and maps the answer (ADR 0003, ADR 0005). The local
/// Partner API plays all four (ADR 0023), and its tests check the same list. API Management runs only in Azure, so these
/// tests read the policies; the smoke run calls them.
/// </summary>
public sealed class ShipperTrackingPolicyTests
{
    public static TheoryData<string> Policies { get; } = new()
    {
        "list-shipper-transport-orders",
        "get-shipper-transport-order",
        "find-shipper-transport-order",
        "get-shipper-proof-of-delivery-file",
    };

    private static XElement Read(string name) => XElement.Parse(Contracts.Read($"PartnerApiPolicies/{name}.policy.xml"));

    private static XElement Inbound(string name) => Read(name).Element("inbound")!;

    private static string Status(XElement refusal) => refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value;

    private static string Metadata(XElement trace, string name) =>
        trace.Elements("metadata").Single(metadata => metadata.Attribute("name")!.Value == name).Attribute("value")!.Value;

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_CreatesTheCorrelationIdBeforeAnyCheck(string name)
    {
        var first = Inbound(name).Elements().First(element => element.Name != "base");

        first.Attribute("name")!.Value.Should().Be("correlationId");
        first.Attribute("value")!.Value.Should().Be("@(Guid.NewGuid().ToString())");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_ChecksInOrder_TheToken_TheRole_TheShipper_ThenTheRateLimit(string name)
    {
        var inbound = Inbound(name).Elements().Select(element => element.Name.LocalName == "choose" ? element.Element("when")!.Attribute("condition")!.Value : element.Name.LocalName + ":" + element.Attribute("name")?.Value).ToList();

        var token = inbound.FindIndex(step => step == "validate-azure-ad-token:");
        var role = inbound.FindIndex(step => step.Contains("\"Tracking.Read\"", StringComparison.Ordinal));
        var shipper = inbound.FindIndex(step => step.Contains("shipperId", StringComparison.Ordinal) && step.Contains("IsNullOrEmpty", StringComparison.Ordinal));
        var rate = inbound.FindIndex(step => step == "rate-limit-by-key:");
        var call = inbound.FindIndex(step => step == "set-backend-service:");

        new[] { token, role, shipper, rate, call }.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.NotContain(-1);
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_ValidatesAnEntraTokenForTheNorviaPartnerApi(string name)
    {
        Inbound(name).Element("validate-azure-ad-token")!.Element("audiences")!.Elements("audience").Select(audience => audience.Value)
            .Should().BeEquivalentTo("{{partner-api-client-id}}");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_RefusesATokenWithoutTheTrackingReadRoleWith403_WhichIsHowACarrierIsRefused(string name)
    {
        var refusal = Inbound(name).Elements("choose").Elements("when").Single(when => when.Attribute("condition")!.Value.Contains("\"Tracking.Read\"", StringComparison.Ordinal));

        Status(refusal).Should().Be("403");
        Metadata(refusal.Element("trace")!, "failureCode").Should().Be("NotAllowedToReadTracking");
        refusal.Element("return-response")!.Element("set-body")!.Value.Should().Contain("This client may not read tracking.");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_TakesTheShipperFromTheTokensAzp_AndRefusesAClientThatIsNoShipperWith403(string name)
    {
        var inbound = Inbound(name);

        inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "shipperId").Attribute("value")!.Value
            .Should().Contain("\"azp\"").And.Contain("{{shippers-by-client-id}}").And.Contain("partnerToken");
        var refusal = inbound.Elements("choose").Elements("when").Single(when => when.Attribute("condition")!.Value.Contains("IsNullOrEmpty", StringComparison.Ordinal));

        Status(refusal).Should().Be("403");
        Metadata(refusal.Element("trace")!, "failureCode").Should().Be("NoShipperForClient");
        refusal.Element("return-response")!.Element("set-body")!.Value.Should().Contain("No Shipper is registered for this client.");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_LimitsTheCallsPerSubscription(string name)
    {
        var limit = Inbound(name).Element("rate-limit-by-key")!;

        limit.Attribute("counter-key")!.Value.Should().Contain("context.Subscription.Id");
        limit.Attribute("calls")!.Value.Should().Be("120");
        limit.Attribute("renewal-period")!.Value.Should().Be("60");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Inbound_CallsTheTms_AsApiManagementsIdentity_WithTheShipperIdAndTheCorrelationId(string name)
    {
        var inbound = Inbound(name);

        inbound.Element("set-backend-service")!.Attribute("backend-id")!.Value.Should().Be("tms-api");
        inbound.Element("authentication-managed-identity")!.Attribute("client-id")!.Value.Should().Be("{{api-management-identity-client-id}}");
        inbound.Element("authentication-managed-identity")!.Attribute("resource")!.Value.Should().Be("{{tms-api-client-id}}");
        inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Shipper-ID").Element("value")!.Value
            .Should().Contain("context.Variables[\"shipperId\"]");
        inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID").Element("value")!.Value
            .Should().Contain("context.Variables[\"correlationId\"]");
        inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "Ocp-Apim-Subscription-Key").Attribute("exists-action")!.Value
            .Should().Be("delete", "the Shipper's key is for API Management, not the TMS");
    }

    [Theory]
    [InlineData("list-shipper-transport-orders", "/v1/shipper-tracking/transport-orders")]
    [InlineData("find-shipper-transport-order", "/v1/shipper-tracking/transport-orders/find")]
    [InlineData("get-shipper-transport-order", "/v1/shipper-tracking/transport-orders/{transportOrderNumber}")]
    [InlineData("get-shipper-proof-of-delivery-file", "/v1/shipper-tracking/transport-orders/{transportOrderNumber}/proof-of-delivery/files/{fileId}")]
    public void Inbound_AsksTheTmsShipperTrackingApi_ForTheSameRoute(string name, string route)
    {
        Inbound(name).Element("rewrite-uri")!.Attribute("template")!.Value.Should().Be(route);
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Backend_WaitsAShortTime_AndDoesNotRetry(string name)
    {
        var policy = Read(name);

        int.Parse(policy.Element("backend")!.Element("forward-request")!.Attribute("timeout")!.Value, System.Globalization.CultureInfo.InvariantCulture).Should().BeInRange(1, 10);
        policy.Descendants("retry").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Outbound_AnswersAGatewayErrorSayingTrackingIsTemporarilyUnavailable_WhenTheTmsFailsWithAServerError(string name)
    {
        var failure = Read(name).Element("outbound")!.Elements("choose").Elements("when")
            .Single(when => when.Attribute("condition")!.Value.Contains(">= 500", StringComparison.Ordinal));

        Status(failure).Should().Be("503");
        failure.Element("return-response")!.Element("set-body")!.Value.Should().Contain("Tracking is temporarily unavailable");
        Metadata(failure.Element("trace")!, "failureCode").Should().Be("TmsUnavailable");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void OnError_AnswersTheSameGatewayError_WhenTheTmsDoesNotAnswerInTime(string name)
    {
        var failure = Read(name).Element("on-error")!.Elements("choose").Elements("when")
            .Single(when => when.Attribute("condition")!.Value.Contains("forward-request", StringComparison.Ordinal));

        Status(failure).Should().Be("503");
        failure.Element("return-response")!.Element("set-body")!.Value.Should().Contain("Tracking is temporarily unavailable");
        Metadata(failure.Element("trace")!, "failureCode").Should().Be("TmsUnavailable");
    }

    [Theory]
    [InlineData("list-shipper-transport-orders", "shipper-tracking-list.liquid")]
    [InlineData("get-shipper-transport-order", "shipper-tracking.liquid")]
    [InlineData("find-shipper-transport-order", "shipper-tracking.liquid")]
    public void Outbound_MapsTheTmsAnswerWithTheLiquidTemplate_AndRefusesAnUnmappedCode(string name, string map)
    {
        var outbound = Read(name).Element("outbound")!;
        var ok = outbound.Elements("choose").Elements("when").Single(when => when.Attribute("condition")!.Value.Contains("== 200", StringComparison.Ordinal) && when.Element("set-body") is not null);

        var setBody = ok.Element("set-body")!;
        setBody.Attribute("template")!.Value.Should().Be("liquid");
        setBody.Attribute("parse-date")!.Value.Should().Be("false");
        setBody.Value.Trim().Should().Be($"[[{map}]]", "the estate Bicep puts the template here");
        Contracts.Read($"PartnerApiMaps/{map}").Should().NotBeEmpty();
        var unmapped = outbound.Elements("choose").Elements("when").Single(when => when.Attribute("condition")!.Value.Contains("Regex", StringComparison.Ordinal));
        unmapped.Attribute("condition")!.Value.Should().Contain("(status|code)");
        Status(unmapped).Should().Be("503");
        Metadata(unmapped.Element("trace")!, "failureCode").Should().Be("TrackingNotMapped");
    }

    [Fact]
    public void Outbound_PassesAProofOfDeliveryFileThroughUnchanged()
    {
        Read("get-shipper-proof-of-delivery-file").Descendants("set-body").Where(body => body.Parent!.Name != "return-response").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Outbound_ReturnsTheCorrelationIdToTheShipper(string name)
    {
        Read(name).Element("outbound")!.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID").Element("value")!.Value
            .Should().Contain("correlationId");
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Traces_AreOnTheShipperTrackingFlow(string name)
    {
        Read(name).Descendants("trace").Should().OnlyContain(trace =>
            trace.Elements("metadata").Any(metadata => metadata.Attribute("name")!.Value == "flow" && metadata.Attribute("value")!.Value == "shipper-tracking"));
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Traces_RecordThePartyAndOutcome_OfEachHop(string name)
    {
        var success = Read(name).Element("outbound")!.Descendants("trace").Single(trace => Metadata(trace, "outcome") == "Succeeded");

        Metadata(success, "partyId").Should().Contain("shipperId");
        Metadata(success, "correlationId").Should().Contain("correlationId");
    }
}
