using System.Text.RegularExpressions;

namespace Norvia.Integration.TransportOrderIntake.UnitTests.PartnerApi;

/// <summary>
/// API Management checks a partner at the edge and passes the Logic App only what it derived itself (ADR 0005, P1). API
/// Management runs only in Azure, so these tests read the policy; the smoke run (infra/smoke-test-partner-api.sh) calls it.
/// </summary>
public sealed class SubmitTransportOrderPolicyTests
{
    private const string Flow = "transport-order-intake";

    [Fact]
    public void Inbound_ValidatesAnEntraTokenForTheNorviaPartnerApi()
    {
        var validation = SubmitTransportOrderPolicy.Inbound.Element("validate-azure-ad-token");

        validation.Should().NotBeNull();
        validation!.Attribute("tenant-id")!.Value.Should().Be("{{entra-tenant-id}}");
        validation.Element("audiences")!.Elements("audience").Select(audience => audience.Value)
            .Should().BeEquivalentTo("{{partner-api-client-id}}");
    }

    [Fact]
    public void Inbound_RefusesATokenWithoutTheSubmitRoleWith403()
    {
        var refusal = SubmitTransportOrderPolicy.Refusal(when => when.Contains("\"TransportOrders.Submit\"", StringComparison.Ordinal));

        refusal.Status.Should().Be(403);
        refusal.Condition.Should().Contain("partnerToken").And.Contain("\"roles\"");
    }

    [Fact]
    public void Inbound_PassesTheShipperMappedFromTheTokensAzp_OverwritingAnyTheCallerSent()
    {
        var shipper = SubmitTransportOrderPolicy.Header("X-Shipper-ID");

        shipper.ExistsAction.Should().Be("override");
        shipper.Value.Should().Be("@((string)context.Variables[\"shipperId\"])");
        SubmitTransportOrderPolicy.Variable("shipperId").Should()
            .Contain("\"azp\"").And.Contain("{{shippers-by-client-id}}").And.Contain("partnerToken");
    }

    [Fact]
    public void Inbound_RefusesAClientThatIsNoShipperWith403()
    {
        // Holding the role isn't enough: a token for a client the seed file names no Shipper for gets no Shipper to act as.
        var refusal = SubmitTransportOrderPolicy.Refusal(when => when.Contains("shipperId", StringComparison.Ordinal));

        refusal.Status.Should().Be(403);
        refusal.Condition.Should().Contain("IsNullOrEmpty");
    }

    [Fact]
    public void Inbound_CreatesTheCorrelationIdBeforeAnyCheck_OverwritingAnyTheCallerSent()
    {
        // Created first, so a refused call has one too and can be found by it (P1).
        var first = SubmitTransportOrderPolicy.Inbound.Elements().First(element => element.Name != "base");
        var correlation = SubmitTransportOrderPolicy.Header("X-Correlation-ID");

        first.Name.LocalName.Should().Be("set-variable");
        first.Attribute("name")!.Value.Should().Be("correlationId");
        first.Attribute("value")!.Value.Should().Be("@(Guid.NewGuid().ToString())");
        correlation.ExistsAction.Should().Be("override");
        correlation.Value.Should().Be("@((string)context.Variables[\"correlationId\"])");
    }

    [Fact]
    public void Inbound_SetsTheHeadersTheIntakeWorkflowReads()
    {
        // Easy Auth sets the X-MS-CLIENT-PRINCIPAL headers, not API Management.
        var read = Regex.Matches(Contracts.Read("IntakeLogicApp/transport-order-intake/workflow.json"), @"\['headers'\]\?\['(X-[A-Za-z-]+)'\]")
            .Select(match => match.Groups[1].Value)
            .Where(name => !name.StartsWith("X-MS-CLIENT-PRINCIPAL", StringComparison.Ordinal))
            .Distinct();
        var set = SubmitTransportOrderPolicy.Inbound.Elements("set-header")
            .Select(header => header.Attribute("name")!.Value)
            .Where(name => name.StartsWith("X-", StringComparison.Ordinal));

        set.Should().BeEquivalentTo(read);
    }

    [Fact]
    public void Inbound_CallsTheIntakeTriggerAsApiManagementsIdentity_WithoutSas()
    {
        var inbound = SubmitTransportOrderPolicy.Inbound;
        var authentication = inbound.Element("authentication-managed-identity");

        inbound.Element("set-backend-service")!.Attribute("backend-id")!.Value.Should().Be("intake-logic-app");
        inbound.Element("rewrite-uri")!.Attribute("template")!.Value
            .Should().Be("/api/transport-order-intake/triggers/When_a_Shipper_books/invoke");
        inbound.Element("rewrite-uri")!.Attribute("copy-unmatched-params")!.Value.Should().Be("false");
        authentication!.Attribute("resource")!.Value.Should().Be("{{intake-trigger-audience}}");
        authentication.Attribute("client-id")!.Value.Should().Be("{{api-management-identity-client-id}}");
    }

    [Fact]
    public void Inbound_EveryRefusal_AnswersWithTheCorrelationIdAndIsTracedAsRefused()
    {
        var refusals = SubmitTransportOrderPolicy.Inbound.Descendants("return-response").ToList();

        refusals.Should().NotBeEmpty();
        foreach (var refusal in refusals)
        {
            var header = refusal.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID");
            header.Element("value")!.Value.Should().Be("@((string)context.Variables[\"correlationId\"])");

            var trace = SubmitTransportOrderPolicy.TrackedKeys(refusal.ElementsBeforeSelf("trace").Single());
            trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
            trace["correlationId"].Should().Be("@((string)context.Variables[\"correlationId\"])");
            trace["flow"].Should().Be(Flow);
            trace["outcome"].Should().Be("Refused");
            trace["failureCode"].Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Inbound_APassedCall_IsTracedWithTheShipperBeforeTheLogicAppRecordsItsOutcome()
    {
        var trace = SubmitTransportOrderPolicy.TrackedKeys(SubmitTransportOrderPolicy.Inbound.Elements("trace").Single());

        trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "partyId");
        trace["correlationId"].Should().Be("@((string)context.Variables[\"correlationId\"])");
        trace["flow"].Should().Be(Flow);
        trace["partyId"].Should().Be("@((string)context.Variables[\"shipperId\"])");
    }

    [Fact]
    public void Outbound_EveryAnswerFromTheLogicApp_CarriesTheCorrelationId_EvenOneFromAFailedRun()
    {
        // A failed run answers with the Logic Apps runtime's error, which carries no header of the workflow's.
        var header = SubmitTransportOrderPolicy.Outbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID");

        header.Attribute("exists-action")!.Value.Should().Be("override");
        header.Element("value")!.Value.Should().Be("@((string)context.Variables[\"correlationId\"])");
    }

    [Fact]
    public void OnError_ACallRefusedForItsKeyOrToken_GetsACorrelationIdAndIsTraced()
    {
        // API Management checks the subscription key before the inbound section runs, so the correlation ID may not exist yet.
        var onError = SubmitTransportOrderPolicy.OnError;
        var created = onError.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "correlationId");
        var header = onError.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID");
        var trace = SubmitTransportOrderPolicy.TrackedKeys(onError.Element("trace")!);

        created.Attribute("value")!.Value.Should().Contain("context.Variables.ContainsKey(\"correlationId\")").And.Contain("Guid.NewGuid()");
        header.Element("value")!.Value.Should().Be("@((string)context.Variables[\"correlationId\"])");
        trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
        trace["flow"].Should().Be(Flow);
        trace["outcome"].Should().Contain("\"Refused\"").And.Contain("\"Failed\"");
        // A key is never sent empty (coding guidelines §7): not every error has a reason, but every one has a source.
        trace["failureCode"].Should().Be("@(string.IsNullOrEmpty(context.LastError.Reason) ? context.LastError.Source : context.LastError.Reason)");
    }
}
