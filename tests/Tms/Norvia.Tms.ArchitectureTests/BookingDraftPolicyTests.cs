using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// API Management's policy for the Norvia Partner API's <c>booking-drafts</c> operation (#459, ADR 0040),
/// infra/estate/partner-api/create-booking-draft.policy.xml: the subscription key, the Entra token, the role, the Shipper from the token's
/// <c>azp</c>, the rate limit, the size cap, no body logged and the correlation ID. API Management runs only in Azure, so these tests read the
/// policy; the smoke run (infra/smoke-test-booking-draft.ps1) calls it. The local stand-in plays the same checks in the same order
/// (LocalPartnerApi's CreateBookingDraftTests).
/// </summary>
public sealed partial class BookingDraftPolicyTests
{
    private const string Flow = "booking-draft";

    private static readonly XElement Policy = XElement.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate/partner-api/create-booking-draft.policy.xml")));

    private static XElement Inbound => Policy.Element("inbound")!;

    private static XElement Outbound => Policy.Element("outbound")!;

    private static XElement OnError => Policy.Element("on-error")!;

    private static string Bicep(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, "infra/estate", file));

    private static XElement Refusal(string failureCode) =>
        Inbound.Elements("choose").Elements("when")
            .Single(when => when.Descendants("metadata").Any(metadata => metadata.Attribute("name")!.Value == "failureCode" && metadata.Attribute("value")!.Value == failureCode));

    private static Dictionary<string, string> Keys(XElement trace) =>
        trace.Elements("metadata").ToDictionary(metadata => metadata.Attribute("name")!.Value, metadata => metadata.Attribute("value")!.Value);

    [Fact]
    public void TheOperation_IsOnThePartnerApi_ThatRequiresASubscriptionKeyInAHeaderOnly()
    {
        var bicep = Bicep("partner-api.bicep");
        var module = Bicep("booking-draft.bicep");

        bicep.Should().Contain("subscriptionRequired: true").And.Contain("query: ''");
        module.Should().Contain("name: 'norvia-partner-api'").And.Contain("urlTemplate: '/booking-drafts'").And.Contain("method: 'POST'");
        module.Should().Contain("loadTextContent('partner-api/create-booking-draft.policy.xml')");
        Inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "Ocp-Apim-Subscription-Key").Attribute("exists-action")!.Value
            .Should().Be("delete", "the key is API Management's, not the host's");
    }

    [Fact]
    public void Inbound_ValidatesAnEntraTokenForTheNorviaPartnerApi()
    {
        var validation = Inbound.Element("validate-azure-ad-token");

        validation!.Attribute("tenant-id")!.Value.Should().Be("{{entra-tenant-id}}");
        validation.Element("audiences")!.Elements("audience").Select(audience => audience.Value).Should().BeEquivalentTo("{{partner-api-client-id}}");
    }

    [Fact]
    public void Inbound_RefusesATokenWithoutTheDraftRoleWith403()
    {
        var when = Refusal("NotAllowedToDraft");

        when.Attribute("condition")!.Value.Should().Contain("\"TransportOrders.Draft\"").And.Contain("partnerToken").And.Contain("\"roles\"");
        when.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("403");
    }

    [Fact]
    public void Inbound_TakesTheShipperFromTheTokensAzp_AndRefusesAClientThatIsNoShipperWith403()
    {
        var shipper = Inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "shipperId").Attribute("value")!.Value;
        var header = Inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Shipper-ID");
        var refusal = Refusal("NoShipperForClient");

        shipper.Should().Contain("\"azp\"").And.Contain("{{shippers-by-client-id}}").And.Contain("partnerToken");
        header.Attribute("exists-action")!.Value.Should().Be("override", "whatever the caller sent");
        header.Element("value")!.Value.Should().Be("@((string)context.Variables[\"shipperId\"])");
        refusal.Attribute("condition")!.Value.Should().Contain("IsNullOrEmpty");
        refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("403");
    }

    [Fact]
    public void Inbound_HasAStricterRateLimitThanABooking_PerSubscription_WithACounterOfItsOwn_AfterTheChecks()
    {
        var limit = Inbound.Element("rate-limit-by-key")!;
        var children = Inbound.Elements().ToList();

        int.Parse(limit.Attribute("calls")!.Value, System.Globalization.CultureInfo.InvariantCulture).Should().Be(10, "the stand-in's DraftsPerMinute");
        limit.Attribute("renewal-period")!.Value.Should().Be("60");
        limit.Attribute("counter-key")!.Value.Should().Contain("context.Subscription.Id").And.Contain("booking-drafts", "its own counter, apart from the tracking operations'");
        children.IndexOf(limit).Should().BeGreaterThan(children.IndexOf(Refusal("NoShipperForClient").Parent!), "a refusal is not counted");
        children.IndexOf(limit).Should().BeLessThan(children.IndexOf(Inbound.Elements("set-backend-service").Single()));
    }

    [Fact]
    public void Inbound_CapsTheRequest_AtFortyKilobytes_With413_BeforeItReachesTheHost()
    {
        var when = Refusal("InputTooLarge");

        when.Attribute("condition")!.Value.Should().Contain("Content-Length").And.Contain("40960").And.Contain("preserveContent: true");
        when.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("413");
        Inbound.Elements().ToList().IndexOf(when.Parent!).Should().BeLessThan(Inbound.Elements().ToList().IndexOf(Inbound.Elements("set-backend-service").Single()));
    }

    [Fact]
    public void Inbound_CreatesTheCorrelationIdFirst_AndSendsItToTheHost_OverwritingAnyTheCallerSent()
    {
        var first = Inbound.Elements().First(element => element.Name != "base");
        var header = Inbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID");

        first.Attribute("name")!.Value.Should().Be("correlationId");
        first.Attribute("value")!.Value.Should().Be("@(Guid.NewGuid().ToString())");
        header.Attribute("exists-action")!.Value.Should().Be("override");
        header.Element("value")!.Value.Should().Be("@((string)context.Variables[\"correlationId\"])");
        Outbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID").Element("value")!.Value
            .Should().Be("@((string)context.Variables[\"correlationId\"])");
    }

    [Fact]
    public void Inbound_CallsTheHostAsApiManagementsIdentity_ForTheHostsOwnApi_WithoutAnyKey()
    {
        var authentication = Inbound.Element("authentication-managed-identity")!;

        Inbound.Element("set-backend-service")!.Attribute("backend-id")!.Value.Should().Be("booking-draft-host");
        Inbound.Element("rewrite-uri")!.Attribute("template")!.Value.Should().Be("/draft/v1/booking-drafts");
        authentication.Attribute("resource")!.Value.Should().Be("{{booking-draft-api-client-id}}");
        authentication.Attribute("client-id")!.Value.Should().Be("{{api-management-identity-client-id}}");
        Policy.ToString().Should().NotContain("subscription-key=").And.NotContain("x-functions-key");
    }

    [Fact]
    public void EveryRefusal_AnswersWithTheCorrelationIdAndTheCode_AndIsTracedAsRefused()
    {
        var refusals = Inbound.Descendants("return-response").ToList();

        refusals.Should().HaveCount(3, "the role, the Shipper and the size cap");
        foreach (var refusal in refusals)
        {
            var header = refusal.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID");
            header.Element("value")!.Value.Should().Be("@((string)context.Variables[\"correlationId\"])");

            var trace = Keys(refusal.ElementsBeforeSelf("trace").Single());
            trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
            trace["flow"].Should().Be(Flow);
            trace["outcome"].Should().Be("Refused");
            refusal.Element("set-body")!.Value.Should().Contain(trace["failureCode"], "the body names the same stable code as the trace");
        }
    }

    [Fact]
    public void APassedCall_IsTracedWithTheShipper()
    {
        var trace = Keys(Inbound.Elements("trace").Single());

        trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "partyId");
        trace["flow"].Should().Be(Flow);
        trace["partyId"].Should().Be("@((string)context.Variables[\"shipperId\"])");
    }

    [Theory]
    [InlineData("StatusCode == 200", "Succeeded", false)]
    [InlineData("StatusCode &gt;= 400", "Refused", true)]
    public void Outbound_TheAnswerIsTracedWithItsOutcome_AndTheHostsOwnCode(string condition, string outcome, bool hasCode)
    {
        var when = Outbound.Element("choose")!.Elements("when").Single(when => when.Attribute("condition")!.Value.Contains(System.Net.WebUtility.HtmlDecode(condition), StringComparison.Ordinal));

        var trace = Keys(when.Element("trace")!);

        trace["flow"].Should().Be(Flow);
        trace["outcome"].Should().Be(outcome);
        trace.ContainsKey("failureCode").Should().Be(hasCode, "a key that doesn't apply is left out, never sent empty");
    }

    [Fact]
    public void Outbound_AnythingElse_IsTracedAsFailed_WithTheAssistantUnavailable()
    {
        var trace = Keys(Outbound.Element("choose")!.Element("otherwise")!.Element("trace")!);

        trace["outcome"].Should().Be("Failed");
        trace["failureCode"].Should().Contain("AgentUnavailable");
    }

    [Fact]
    public void OnError_AHostThatCannotBeReached_IsTheAssistantUnavailable_WithTheCorrelationId_AndIsTraced()
    {
        var trace = Keys(OnError.Element("trace")!);
        var unavailable = OnError.Element("choose")!.Element("when")!;

        OnError.Elements("set-variable").Single().Attribute("value")!.Value.Should().Contain("ContainsKey(\"correlationId\")").And.Contain("Guid.NewGuid()");
        trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
        trace["flow"].Should().Be(Flow);
        unavailable.Attribute("condition")!.Value.Should().Contain("StatusCode &gt;= 500".Replace("&gt;", ">"));
        unavailable.Element("set-status")!.Attribute("code")!.Value.Should().Be("503");
        unavailable.Element("set-body")!.Value.Should().Contain("AgentUnavailable").And.Contain("The assistant is unavailable.");
    }

    [Fact]
    public void NoBodyIsLogged_NotInATrace_NotByAnyPolicy_AndNotByTheServicesDiagnostics()
    {
        var traces = Policy.Descendants("trace").Select(trace => trace.ToString()).ToList();
        var diagnostics = DiagnosticsBlock().Match(Bicep("partner-api.bicep")).Value;

        traces.Should().NotBeEmpty();
        // The request body is read for its size and the response body for its code, neither of them in a trace.
        traces.Should().OnlyContain(trace => !trace.Contains("Request.Body", StringComparison.Ordinal) && !trace.Contains("Response.Body", StringComparison.Ordinal) || trace.Contains("failureCode", StringComparison.Ordinal));
        Policy.ToString().Should().NotContain("log-to-eventhub").And.NotContain("send-request").And.NotContain("send-one-way-request");
        diagnostics.Should().NotBeEmpty().And.NotMatchRegex(@"\bbody\s*:", "a body setting would log the Shipper's text, which holds a Consignee's personal data");
    }

    [Fact]
    public void TheCodesAndTheFlow_AreTheOnesTheStandInUses()
    {
        var local = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Integration/Norvia.Integration.LocalPartnerApi/EdgeRule.cs"));

        local.Should().Contain("\"TransportOrders.Draft\"").And.Contain("\"NotAllowedToDraft\"").And.Contain("\"NoShipperForClient\"");
        Keys(Inbound.Descendants("trace").First())["flow"].Should().Be(Flow);
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src/Integration/Norvia.Integration.LocalSupport/TrackingKeys.cs")).Should().Contain($"BookingDraft = \"{Flow}\"");
    }

    [GeneratedRegex(@"(?ms)^resource diagnostics 'Microsoft\.ApiManagement/service/diagnostics@[^']+' = \{.*?^\}$")]
    private static partial Regex DiagnosticsBlock();
}
