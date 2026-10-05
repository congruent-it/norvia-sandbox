using System.Xml.Linq;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// API Management accepts a Carrier's tracking message at the edge, puts it on the tracking queue with the tracking
/// contract's application properties and answers 202, without calling the Logic App (ADR 0008, P5). API Management runs
/// only in Azure, so these tests read the policy; the smoke run calls it.
/// </summary>
public sealed class SubmitCarrierTrackingPolicyTests
{
    private static readonly XElement Policy = XElement.Parse(Contracts.Read("PartnerApiPolicies/submit-carrier-tracking.policy.xml"));
    private static readonly XElement Inbound = Policy.Element("inbound")!;

    private static XElement Send => Inbound.Element("send-request")!;

    private static string Header(string name) =>
        Send.Elements("set-header").Single(header => header.Attribute("name")!.Value == name).Element("value")!.Value;

    [Fact]
    public void Inbound_CreatesTheCorrelationIdBeforeAnyCheck()
    {
        var first = Inbound.Elements().First(element => element.Name != "base");

        first.Attribute("name")!.Value.Should().Be("correlationId");
        first.Attribute("value")!.Value.Should().Be("@(Guid.NewGuid().ToString())");
    }

    [Fact]
    public void Inbound_ValidatesAnEntraTokenForTheNorviaPartnerApi()
    {
        var validation = Inbound.Element("validate-azure-ad-token")!;

        validation.Element("audiences")!.Elements("audience").Select(audience => audience.Value).Should().BeEquivalentTo("{{partner-api-client-id}}");
    }

    [Fact]
    public void Inbound_RefusesATokenWithoutTheTrackingSubmitRoleWith403()
    {
        var refusal = Inbound.Elements("choose").Elements("when")
            .Single(when => when.Attribute("condition")!.Value.Contains("\"Tracking.Submit\"", StringComparison.Ordinal));

        refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("403");
    }

    [Fact]
    public void Inbound_TakesTheCarrierFromTheTokensAzp_AndRefusesAClientThatIsNoCarrierWith403()
    {
        Inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "carrierId").Attribute("value")!.Value
            .Should().Contain("\"azp\"").And.Contain("{{carriers-by-client-id}}").And.Contain("partnerToken");
        var refusal = Inbound.Elements("choose").Elements("when").Single(when => when.Attribute("condition")!.Value.Contains("IsNullOrEmpty", StringComparison.Ordinal) && when.Attribute("condition")!.Value.Contains("carrierId", StringComparison.Ordinal));

        refusal.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("403");
    }

    [Fact]
    public void Inbound_EnqueuesToTheTrackingQueue_AsApiManagementsIdentity_AndCallsNoLogicApp()
    {
        Send.Element("set-url")!.Value.Should().Contain("{{service-bus-namespace}}").And.Contain("{{tracking-queue-name}}/messages");
        Send.Element("set-method")!.Value.Should().Be("POST");
        Send.Element("authentication-managed-identity")!.Attribute("resource")!.Value.Should().Be("https://servicebus.azure.net");
        Policy.ToString().Should().NotContain("set-backend-service");
    }

    [Fact]
    public void Enqueue_SetsTheTrackingContractsApplicationProperties()
    {
        Header("correlationId").Should().Contain("context.Variables[\"correlationId\"]");
        Header("flow").Should().Be("carrier-tracking");
        Header("partyId").Should().Contain("context.Variables[\"carrierId\"]");
        Header("sourceMessageId").Should().Contain("context.Variables[\"sourceMessageId\"]");
        Inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "sourceMessageId").Attribute("value")!.Value
            .Should().Contain("\"eventId\"", "the Carrier's event ID is the source message ID");
    }

    [Fact]
    public void Enqueue_LeavesOutTheSourceMessageId_WhenTheBodyHasNone()
    {
        Send.Elements("set-header").Single(header => header.Attribute("name")!.Value == "sourceMessageId").Attribute("exists-action")!.Value
            .Should().Contain("delete");
    }

    [Fact]
    public void Enqueue_UsesTheCorrelationIdAsTheMessageId_SoTheMessageIsFoundByIt()
    {
        Header("BrokerProperties").Should().Contain("MessageId").And.Contain("context.Variables[\"correlationId\"]");
    }

    [Fact]
    public void Inbound_AnswersAccepted202WithTheCorrelationId_OnceQueued()
    {
        var answer = Inbound.Elements("return-response").Last();

        answer.Element("set-status")!.Attribute("code")!.Value.Should().Be("202");
        answer.Element("set-header")!.Value.Should().Contain("correlationId");
        Inbound.Elements().Last().Name.LocalName.Should().Be("return-response");
    }

    [Fact]
    public void Inbound_AnswersUnavailable503_WhenTheMessageCouldNotBeQueued_SoTheCarrierSendsItAgain()
    {
        var failure = Inbound.Elements("choose").Elements("when")
            .Single(when => when.Attribute("condition")!.Value.Contains("enqueued", StringComparison.Ordinal));

        failure.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value.Should().Be("503");
        failure.Element("trace")!.Elements("metadata").Single(metadata => metadata.Attribute("name")!.Value == "outcome").Attribute("value")!.Value.Should().Be("Failed");
    }

    [Fact]
    public void Traces_AreOnTheCarrierTrackingFlow()
    {
        Policy.Descendants("trace").Should().OnlyContain(trace =>
            trace.Elements("metadata").Any(metadata => metadata.Attribute("name")!.Value == "flow" && metadata.Attribute("value")!.Value == "carrier-tracking"));
    }
}
