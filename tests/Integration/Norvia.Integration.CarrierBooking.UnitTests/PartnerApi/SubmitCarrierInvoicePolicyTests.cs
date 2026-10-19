using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// API Management checks a Carrier at the edge and passes the Logic App only what it derived itself (ADR 0005, E2).
/// API Management runs only in Azure, so these tests read the policy; the smoke run calls it.
/// </summary>
public sealed class SubmitCarrierInvoicePolicyTests
{
    private const string Flow = "carrier-invoice";
    private const string CorrelationId = "@((string)context.Variables[\"correlationId\"])";

    [Fact]
    public void Inbound_ValidatesAnEntraTokenForTheNorviaPartnerApi()
    {
        var validation = SubmitCarrierInvoicePolicy.Inbound.Element("validate-azure-ad-token");

        validation!.Attribute("tenant-id")!.Value.Should().Be("{{entra-tenant-id}}");
        validation.Element("audiences")!.Elements("audience").Select(audience => audience.Value).Should().BeEquivalentTo("{{partner-api-client-id}}");
    }

    [Fact]
    public void Inbound_RefusesATokenWithoutTheSubmitRoleWith403()
    {
        var refusal = SubmitCarrierInvoicePolicy.Refusal(when => when.Contains("\"CarrierInvoices.Submit\"", StringComparison.Ordinal));

        refusal.Status.Should().Be(403);
        refusal.Condition.Should().Contain("partnerToken").And.Contain("\"roles\"");
    }

    [Fact]
    public void Inbound_PassesTheCarrierMappedFromTheTokensAzp_OverwritingAnyTheCallerSent()
    {
        var carrier = SubmitCarrierInvoicePolicy.Header("X-Carrier-ID");

        carrier.ExistsAction.Should().Be("override");
        carrier.Value.Should().Be("@((string)context.Variables[\"carrierId\"])");
        SubmitCarrierInvoicePolicy.Variable("carrierId").Should()
            .Contain("\"azp\"").And.Contain("{{carriers-by-client-id}}").And.Contain("partnerToken");
    }

    [Fact]
    public void Inbound_RefusesAClientThatIsNoCarrierWith403()
    {
        // Holding the role isn't enough: a token for a client the seed file names no Carrier for gets no Carrier to act as.
        var refusal = SubmitCarrierInvoicePolicy.Refusal(when => when.Contains("carrierId", StringComparison.Ordinal));

        refusal.Status.Should().Be(403);
        refusal.Condition.Should().Contain("IsNullOrEmpty");
    }

    [Fact]
    public void Inbound_CreatesANewCorrelationIdForTheInvoiceBeforeAnyCheck_OverwritingAnyTheCallerSent()
    {
        var first = SubmitCarrierInvoicePolicy.Inbound.Elements().First(element => element.Name != "base");
        var correlation = SubmitCarrierInvoicePolicy.Header("X-Correlation-ID");

        first.Attribute("name")!.Value.Should().Be("correlationId");
        first.Attribute("value")!.Value.Should().Be("@(Guid.NewGuid().ToString())");
        correlation.ExistsAction.Should().Be("override");
        correlation.Value.Should().Be(CorrelationId);
    }

    [Fact]
    public void Inbound_SetsTheHeadersTheInvoiceWorkflowReads()
    {
        var read = Regex.Matches(Contracts.Read("CarrierInvoiceLogicApp/carrier-invoice/workflow.json"), @"\['headers'\]\?\['(X-[A-Za-z-]+)'\]")
            .Select(match => match.Groups[1].Value)
            .Where(name => !name.StartsWith("X-MS-CLIENT-PRINCIPAL", StringComparison.Ordinal))
            .Distinct();
        var set = SubmitCarrierInvoicePolicy.Inbound.Elements("set-header")
            .Select(header => header.Attribute("name")!.Value)
            .Where(name => name.StartsWith("X-", StringComparison.Ordinal));

        set.Should().BeEquivalentTo(read);
    }

    [Fact]
    public void Inbound_CallsTheCarrierInvoiceTriggerAsApiManagementsIdentity_WithoutSas()
    {
        var inbound = SubmitCarrierInvoicePolicy.Inbound;
        var authentication = inbound.Element("authentication-managed-identity");

        inbound.Element("set-backend-service")!.Attribute("backend-id")!.Value.Should().Be("carrier-invoice-logic-app");
        inbound.Element("rewrite-uri")!.Attribute("template")!.Value.Should().Be("/api/carrier-invoice/triggers/When_a_Carrier_invoices/invoke");
        inbound.Element("rewrite-uri")!.Attribute("copy-unmatched-params")!.Value.Should().Be("false");
        authentication!.Attribute("resource")!.Value.Should().Be("{{carrier-invoice-trigger-audience}}");
        authentication.Attribute("client-id")!.Value.Should().Be("{{api-management-identity-client-id}}");
    }

    [Fact]
    public void Inbound_EveryRefusal_AnswersWithTheCorrelationIdAndIsTracedAsRefused()
    {
        var refusals = SubmitCarrierInvoicePolicy.Inbound.Descendants("return-response").ToList();

        refusals.Should().NotBeEmpty();
        foreach (var refusal in refusals)
        {
            refusal.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID").Element("value")!.Value.Should().Be(CorrelationId);
            var trace = SubmitCarrierInvoicePolicy.TrackedKeys(refusal.ElementsBeforeSelf("trace").Single());
            trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
            trace["flow"].Should().Be(Flow);
            trace["outcome"].Should().Be("Refused");
        }
    }

    [Fact]
    public void Inbound_APassedCall_IsTracedWithTheCarrier()
    {
        var trace = SubmitCarrierInvoicePolicy.TrackedKeys(SubmitCarrierInvoicePolicy.Inbound.Elements("trace").Single());

        trace.Keys.Should().BeEquivalentTo("correlationId", "flow", "partyId");
        trace["flow"].Should().Be(Flow);
        trace["partyId"].Should().Be("@((string)context.Variables[\"carrierId\"])");
    }

    [Fact]
    public void Outbound_EveryAnswerFromTheLogicApp_CarriesTheCorrelationId_EvenOneFromAFailedRun()
    {
        var header = SubmitCarrierInvoicePolicy.Outbound.Elements("set-header").Single(header => header.Attribute("name")!.Value == "X-Correlation-ID");

        header.Attribute("exists-action")!.Value.Should().Be("override");
        header.Element("value")!.Value.Should().Be(CorrelationId);
    }

    [Fact]
    public void Outbound_AReceivedInvoice_IsTracedAsSucceeded_WhetherNewOrARepeat()
    {
        var when = SubmitCarrierInvoicePolicy.Outbound.Elements("choose").Elements("when").First();

        when.Attribute("condition")!.Value.Should().Contain("StatusCode == 200").And.Contain("StatusCode == 201");
        var trace = SubmitCarrierInvoicePolicy.TrackedKeys(when.Element("trace")!);
        trace["outcome"].Should().Be("Succeeded");
        trace["flow"].Should().Be(Flow);
        trace["partyId"].Should().Be("@((string)context.Variables[\"carrierId\"])");
        trace.Should().NotContainKey("failureCode", "a key that doesn't apply is left out, never sent empty");
    }

    [Fact]
    public void Outbound_ARefusalOfTheInvoice_IsTracedAsRefused_WithTheStableCodes()
    {
        var when = SubmitCarrierInvoicePolicy.Outbound.Elements("choose").Elements("when").Last();

        when.Attribute("condition")!.Value.Should().Contain("== 400").And.Contain("== 403").And.Contain("== 409").And.Contain("== 422");
        var trace = SubmitCarrierInvoicePolicy.TrackedKeys(when.Element("trace")!);
        trace["outcome"].Should().Be("Refused");
        trace["failureCode"].Should().Contain("InvoiceNumberUsed").And.Contain("CarrierNotFound").And.Contain("CarrierNotAllowed").And.Contain("CarrierInvoiceUnreadable");
    }

    [Fact]
    public void Outbound_AnyOtherAnswer_IsTracedAsFailed_BecauseNothingWasReceived()
    {
        var otherwise = SubmitCarrierInvoicePolicy.Outbound.Elements("choose").Elements("otherwise").Single();

        var trace = SubmitCarrierInvoicePolicy.TrackedKeys(otherwise.Element("trace")!);

        trace["outcome"].Should().Be("Failed");
        trace["failureCode"].Should().Be("InvoiceNotReceived");
    }

    [Fact]
    public void OnError_ACallRefusedForItsKeyOrToken_GetsACorrelationIdAndIsTraced()
    {
        var onError = SubmitCarrierInvoicePolicy.OnError;
        var created = onError.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "correlationId");
        var trace = SubmitCarrierInvoicePolicy.TrackedKeys(onError.Element("trace")!);

        created.Attribute("value")!.Value.Should().Contain("context.Variables.ContainsKey(\"correlationId\")").And.Contain("Guid.NewGuid()");
        trace["flow"].Should().Be(Flow);
        trace["failureCode"].Should().Be("@(string.IsNullOrEmpty(context.LastError.Reason) ? context.LastError.Source : context.LastError.Reason)");
    }
}
