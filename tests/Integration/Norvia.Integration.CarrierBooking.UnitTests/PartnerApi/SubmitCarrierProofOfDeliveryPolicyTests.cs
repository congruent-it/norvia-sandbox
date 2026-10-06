using System.Xml.Linq;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// API Management accepts a Carrier's Proof of delivery at the edge by claim check (ADR 0009, P6): it stores the body
/// unparsed as a blob, only then puts the reference with the tracking contract's application properties on the Proof of
/// delivery queue, and answers 202, without calling the Logic App. API Management runs only in Azure, so these tests read
/// the policy; the smoke run calls it.
/// </summary>
public sealed class SubmitCarrierProofOfDeliveryPolicyTests
{
    private static readonly XElement Policy = XElement.Parse(Contracts.Read("PartnerApiPolicies/submit-carrier-proof-of-delivery.policy.xml"));
    private static readonly XElement Inbound = Policy.Element("inbound")!;

    private static XElement Send(string variable) =>
        Inbound.Elements("send-request").Single(send => send.Attribute("response-variable-name")!.Value == variable);

    private static string Header(XElement send, string name) =>
        send.Elements("set-header").Single(header => header.Attribute("name")!.Value == name).Element("value")!.Value;

    private static XElement When(string conditionPart) =>
        Inbound.Elements("choose").Elements("when").Single(when => when.Attribute("condition")!.Value.Contains(conditionPart, StringComparison.Ordinal));

    private static string StatusOf(XElement when) => when.Element("return-response")!.Element("set-status")!.Attribute("code")!.Value;

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
        Inbound.Element("validate-azure-ad-token")!.Element("audiences")!.Elements("audience").Select(audience => audience.Value)
            .Should().BeEquivalentTo("{{partner-api-client-id}}");
    }

    [Fact]
    public void Inbound_RefusesATokenWithoutTheProofsOfDeliverySubmitRoleWith403()
    {
        StatusOf(When("\"ProofsOfDelivery.Submit\"")).Should().Be("403");
    }

    [Fact]
    public void Inbound_RefusesABodyOver25MegabytesWith413_BeforeStoringAnything()
    {
        var tooLarge = When("26214400");

        StatusOf(tooLarge).Should().Be("413");
        tooLarge.Attribute("condition")!.Value.Should().Contain("Content-Length");
        Inbound.Elements().ToList().IndexOf(tooLarge.Parent!).Should().BeLessThan(Inbound.Elements().ToList().IndexOf(Send("stored")));
        tooLarge.Element("trace")!.Elements("metadata").Single(metadata => metadata.Attribute("name")!.Value == "outcome").Attribute("value")!.Value.Should().Be("Refused");
    }

    [Fact]
    public void Inbound_TakesTheCarrierFromTheTokensAzp_AndRefusesAClientThatIsNoCarrierWith403()
    {
        Inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "carrierId").Attribute("value")!.Value
            .Should().Contain("\"azp\"").And.Contain("{{carriers-by-client-id}}").And.Contain("partnerToken");

        StatusOf(When("IsNullOrEmpty((string)context.Variables[\"carrierId\"])")).Should().Be("403");
    }

    [Fact]
    public void Store_WritesTheBodyUnparsedAsABlobNamedForTheCorrelationId_AsApiManagementsIdentity()
    {
        var store = Send("stored");

        store.Element("set-url")!.Value.Should().Contain("{{inbound-storage-account}}").And.Contain("{{inbound-container-name}}").And.Contain("correlationId");
        store.Element("set-method")!.Value.Should().Be("PUT");
        Header(store, "x-ms-blob-type").Should().Be("BlockBlob");
        store.Element("set-body")!.Value.Should().Contain("As<byte[]>(preserveContent: true)", "the body is stored as received, not parsed and written back");
        store.Element("authentication-managed-identity")!.Attribute("resource")!.Value.Should().Be("https://storage.azure.com");
    }

    [Fact]
    public void Enqueue_RunsOnlyAfterTheBlobIsStored_AndAnUnstoredBodyIsAServerError()
    {
        var elements = Inbound.Elements().ToList();
        var failedStore = When("context.Variables[\"stored\"]");

        elements.IndexOf(Send("enqueued")).Should().BeGreaterThan(elements.IndexOf(failedStore.Parent!));
        elements.IndexOf(failedStore.Parent!).Should().BeGreaterThan(elements.IndexOf(Send("stored")));
        StatusOf(failedStore).Should().Be("503");
    }

    [Fact]
    public void Enqueue_PutsOnlyTheBlobReferenceOnTheProofOfDeliveryQueue_AsApiManagementsIdentity_AndCallsNoLogicApp()
    {
        var enqueue = Send("enqueued");

        enqueue.Element("set-url")!.Value.Should().Contain("{{service-bus-namespace}}").And.Contain("{{proof-of-delivery-queue-name}}/messages");
        enqueue.Element("set-method")!.Value.Should().Be("POST");
        enqueue.Element("set-body")!.Value.Should().Contain("blobName").And.Contain("container").And.NotContain("Request.Body", "the Proof of delivery itself never goes on the queue");
        enqueue.Element("authentication-managed-identity")!.Attribute("resource")!.Value.Should().Be("https://servicebus.azure.net");
        Policy.ToString().Should().NotContain("set-backend-service");
    }

    [Fact]
    public void Enqueue_SetsTheTrackingContractsApplicationProperties()
    {
        var enqueue = Send("enqueued");

        Header(enqueue, "correlationId").Should().Contain("context.Variables[\"correlationId\"]");
        Header(enqueue, "flow").Should().Be("carrier-proof-of-delivery");
        Header(enqueue, "partyId").Should().Contain("context.Variables[\"carrierId\"]");
        Header(enqueue, "sourceMessageId").Should().Contain("context.Variables[\"sourceMessageId\"]");
        Inbound.Elements("set-variable").Single(variable => variable.Attribute("name")!.Value == "sourceMessageId").Attribute("value")!.Value
            .Should().Contain("\"documentId\"", "the Carrier's document ID is the source message ID");
    }

    [Fact]
    public void Enqueue_LeavesOutTheSourceMessageId_WhenTheBodyHasNone()
    {
        Send("enqueued").Elements("set-header").Single(header => header.Attribute("name")!.Value == "sourceMessageId").Attribute("exists-action")!.Value
            .Should().Contain("delete");
    }

    [Fact]
    public void Enqueue_UsesTheCorrelationIdAsTheMessageId_SoTheMessageIsFoundByIt()
    {
        Header(Send("enqueued"), "BrokerProperties").Should().Contain("MessageId").And.Contain("context.Variables[\"correlationId\"]");
    }

    [Fact]
    public void Inbound_AnswersAServerError503_WhenTheReferenceCouldNotBeQueued_SoTheCarrierSendsItAgain()
    {
        var failure = When("context.Variables[\"enqueued\"]");

        StatusOf(failure).Should().Be("503");
        failure.Element("trace")!.Elements("metadata").Single(metadata => metadata.Attribute("name")!.Value == "outcome").Attribute("value")!.Value.Should().Be("Failed");
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
    public void Traces_AreOnTheCarrierProofOfDeliveryFlow()
    {
        Policy.Descendants("trace").Should().OnlyContain(trace =>
            trace.Elements("metadata").Any(metadata => metadata.Attribute("name")!.Value == "flow" && metadata.Attribute("value")!.Value == "carrier-proof-of-delivery"));
    }
}
