using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ShipperInvoiceLogicApp;

/// <summary>
/// The E1 Shipper invoice Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and E1. The Logic Apps
/// runtime runs only in Azure, so these tests read the definition; the map it calls is tested on its own, and the local worker plays
/// the same steps (ADR 0030, ADR 0043).
/// </summary>
public sealed class ShipperInvoiceWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_invoice')?['correlationId']}";

    /// <summary>A Credit note's report goes to the ERP's Credit notes, an invoice's to its Shipper invoices.</summary>
    private const string ReportPath = "@{if(startsWith(outputs('Track_the_invoice')?['documentNumber'], 'CN-'), 'credit-notes', 'shipper-invoices')}";

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheShipperInvoiceSubscriptionOnTheInvoicingEventsTopic()
    {
        var parameters = ShipperInvoiceWorkflow.Trigger["inputs"]!["parameters"]!;

        ShipperInvoiceWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ServiceProvider");
        ShipperInvoiceWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("peekLockTopicMessages");
        parameters["topicName"]!.GetValue<string>().Should().Be("invoicing-events");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("shipper-invoice");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheInvoicesCorrelationId() =>
        ShipperInvoiceWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['userProperties']?['correlationId']}");

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        ShipperInvoiceWorkflow.Actions.Where(action => action.Key != ShipperInvoiceWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
        Statuses(ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.MapAction), ShipperInvoiceWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheInvoiceOrCreditNoteTheMessageCarries_LeavingOutAMissingOne()
    {
        var inputs = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "documentNumber", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => (expression.StartsWith("@json(base64ToString(triggerBody()?['contentData']))?['", StringComparison.Ordinal)
                    || expression.StartsWith("@coalesce(json(base64ToString(triggerBody()?['contentData']))?['", StringComparison.Ordinal))
                && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().Contain("?['invoice']?['shipper']?['shipperId']").And.Contain("?['creditNote']?['shipper']?['shipperId']");
        inputs["documentNumber"]!.GetValue<string>().Should().Contain("?['invoice']?['invoiceNumber']").And.Contain("?['creditNote']?['creditNoteNumber']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "documentNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(ShipperInvoiceWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = ShipperInvoiceWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == ShipperInvoiceWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain(
            [ShipperInvoiceWorkflow.Tracking, ShipperInvoiceWorkflow.MapAction, ShipperInvoiceWorkflow.Send, ShipperInvoiceWorkflow.ReportSent, ShipperInvoiceWorkflow.ReportNotDelivered]);
    }

    [Fact]
    public void Map_RunsTheShipperDocumentMapOnTheMessageContent_AndTracksAnUnreadableDocumentAsFailed()
    {
        var map = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.MapAction);

        map["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapShipperDocument");
        map["inputs"]!["parameters"]!["message"]!.GetValue<string>().Should().Be("@{triggerBody()?['contentData']}");
        map["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("ShipperInvoiceIssuedUnreadable");
    }

    [Fact]
    public void UnreadableInvoice_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = ShipperInvoiceWorkflow.Action("Dead_letter_the_unreadable_invoice");
        var parameters = deadLetter["inputs"]!["parameters"]!;

        Statuses(deadLetter, ShipperInvoiceWorkflow.MapAction).Should().BeEquivalentTo("Failed");
        deadLetter["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("deadLetterTopicMessageV2");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("shipper-invoice");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['lockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("ShipperInvoiceIssuedUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("ShipperInvoiceIssuedUnreadable");
        ShipperInvoiceWorkflow.Action("Fail_the_run_on_the_unreadable_invoice")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void MappedInvoice_IsCompletedBeforeAnythingIsSent_SoAFailedRunIsNotRedelivered()
    {
        Statuses(ShipperInvoiceWorkflow.Action("Complete_the_message"), ShipperInvoiceWorkflow.MapAction).Should().BeEquivalentTo("Succeeded");
        Statuses(ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Credential), "Complete_the_message").Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_IsReadFromKeyVaultByTheShippersId_AndItsOutputIsSecure()
    {
        var credential = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Credential);

        credential["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/keyVault");
        credential["inputs"]!["parameters"]!["secretName"]!.GetValue<string>().Should().Be("shipper-endpoint-key-@{outputs('Track_the_invoice')?['partyId']}");
        credential["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>()).Should().Equal("outputs");
    }

    [Fact]
    public void Send_PostsTheMappedInvoiceToTheShippersInvoiceEndpointInThePortal_WithTheInvoicesCorrelationIdAndTheCredential()
    {
        var send = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Send);
        var inputs = send["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('partnerPortalBaseUrl')}/shipper/v1/@{outputs('Track_the_invoice')?['partyId']}/invoices");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["headers"]!["X-Api-Key"]!.GetValue<string>().Should().Be("@{body('Get_the_Shipper_endpoint_credential')?['value']}");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_invoice'))");
        Statuses(send, ShipperInvoiceWorkflow.Credential).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_NeverAppearsInTheWorkflow_OnlyAsTheOutputOfTheStepThatReadsIt()
    {
        ShipperInvoiceWorkflow.Operations().Where(operation => operation.Operation.ToJsonString().Contains(ShipperInvoiceWorkflow.Credential, StringComparison.Ordinal))
            .Select(operation => operation.Name).Should().Equal(ShipperInvoiceWorkflow.Send);
    }

    [Fact]
    public void SecureData_IsOnlyOnTheStepsThatHandleTheCredential()
    {
        // Anywhere else it would hide the body the Integration operator needs (coding guidelines §7).
        var secure = ShipperInvoiceWorkflow.Operations().Where(operation => operation.Operation["runtimeConfiguration"]?["secureData"] is not null)
            .ToDictionary(operation => operation.Name, operation => operation.Operation["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Single()!.GetValue<string>());

        secure.Should().BeEquivalentTo(new Dictionary<string, string> { [ShipperInvoiceWorkflow.Credential] = "outputs", [ShipperInvoiceWorkflow.Send] = "inputs" });
    }

    [Theory]
    [InlineData(ShipperInvoiceWorkflow.Send)]
    [InlineData(ShipperInvoiceWorkflow.ReportSent)]
    [InlineData(ShipperInvoiceWorkflow.ReportNotDelivered)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AsTheOtherLogicAppsDo(string name)
    {
        var retry = ShipperInvoiceWorkflow.Action(name)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Theory]
    [InlineData(ShipperInvoiceWorkflow.Send)]
    [InlineData(ShipperInvoiceWorkflow.ReportSent)]
    [InlineData(ShipperInvoiceWorkflow.ReportNotDelivered)]
    public void Calls_CarryTheInvoicesCorrelationId(string name) =>
        ShipperInvoiceWorkflow.Action(name)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);

    [Fact]
    public void ReportSent_CallsTheTheErpsReportOperationAsTheShipperInvoiceIdentity()
    {
        var report = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.ReportSent);
        var inputs = report["inputs"]!;

        report["runAfter"]!.AsObject().Should().BeEmpty("it sits in the Report branch that runs once the Shipper acknowledged");
        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be($"@{{parameters('erpBaseUrl')}}/v1/{ReportPath}/@{{outputs('Track_the_invoice')?['documentNumber']}}/report-sent");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('shipperInvoiceIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('erpAudience')");
    }

    [Fact]
    public void Tracking_ACreditNoteMessage_GivesTheCreditNotesNumberAndShipper()
    {
        var message = Contracts.Read("CanonicalModel/samples/credit-note-issued/partial-credit.json");
        var inputs = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Tracking)["inputs"]!.AsObject();

        ShipperInvoiceSampleTests.Resolve(JsonNode.Parse(message)!, inputs["documentNumber"]!.GetValue<string>()).Should().Be("CN-2030-000001");
        ShipperInvoiceSampleTests.Resolve(JsonNode.Parse(message)!, inputs["partyId"]!.GetValue<string>()).Should().Be("SHP-0001");
    }

    [Fact]
    public void Report_RunsWhateverTheShipperAnswered_AndReportsSentOnASuccess_AndNotDeliveredOtherwise()
    {
        var report = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Report);

        Statuses(report, ShipperInvoiceWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        report["actions"]!.AsObject().Select(action => action.Key).Should().Equal(ShipperInvoiceWorkflow.ReportSent);
        report["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Contain(ShipperInvoiceWorkflow.ReportNotDelivered);
        var expression = report["expression"]!.ToJsonString();
        expression.Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
    }

    [Fact]
    public void ReportNotDelivered_CallsTheErpsNotDeliveredOperationAsTheShipperInvoiceIdentity()
    {
        var inputs = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.ReportNotDelivered)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be($"@{{parameters('erpBaseUrl')}}/v1/{ReportPath}/@{{outputs('Track_the_invoice')?['documentNumber']}}/report-not-delivered");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('shipperInvoiceIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('erpAudience')");
    }

    [Fact]
    public void ReportNotDelivered_CarriesTheStatusCodeAndAMessageOfAtMost500Characters()
    {
        var body = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.ReportNotDelivered)["inputs"]!["body"]!.AsObject();

        body.Select(property => property.Key).Should().BeEquivalentTo("statusCode", "message");
        body["statusCode"]!.GetValue<string>().Should().Contain("coalesce(actions('Send_to_the_Shipper')?['outputs']?['statusCode'], 0)");
        body["message"]!.GetValue<string>().Should().Contain("min(500,");
    }

    [Fact]
    public void ErpReports_TrackTheOutcomeAndTheFailureCodeOfTheCall()
    {
        foreach (var name in new[] { ShipperInvoiceWorkflow.ReportSent, ShipperInvoiceWorkflow.ReportNotDelivered })
        {
            var tracked = ShipperInvoiceWorkflow.Action(name)["trackedProperties"]!.AsObject();

            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "documentNumber", "correlationId", "partyId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("ErpUnavailable").And.Contain("ErpCallRefused").And.Contain($"actions('{name}')");
        }
    }

    [Fact]
    public void ReportSent_TracksTheOutcomeAndTheFailureCodeOfTheCall()
    {
        var tracked = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.ReportSent)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "documentNumber", "correlationId", "partyId"]);
        tracked["failureCode"]!.GetValue<string>().Should().Contain("ErpUnavailable").And.Contain("ErpCallRefused");
    }

    [Fact]
    public void Send_TracksTheShippersFailureAsUnavailableOrRefused()
    {
        var failureCode = ShipperInvoiceWorkflow.Action(ShipperInvoiceWorkflow.Send)["trackedProperties"]!["failureCode"]!.GetValue<string>();

        failureCode.Should().Contain("ShipperUnavailable").And.Contain("ShipperRefused");
    }

    [Fact]
    public void Calls_AreTheShippersEndpointAndTheErpsReport_AndNothingElse() =>
        ShipperInvoiceWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(ShipperInvoiceWorkflow.Send, ShipperInvoiceWorkflow.ReportSent, ShipperInvoiceWorkflow.ReportNotDelivered);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        ShipperInvoiceWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = ShipperInvoiceWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("erpBaseUrl", "erpAudience", "shipperInvoiceIdentityId", "partnerPortalBaseUrl");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_UseTheShipperInvoiceIdentity_NotAKey()
    {
        var connections = ShipperInvoiceWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("serviceBus", "keyVault");
        foreach (var (_, connection) in connections)
        {
            var authProvider = connection!["parameterValues"]!["authProvider"]!;
            authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('SHIPPER_INVOICE_IDENTITY_ID')");
        }

        connections["serviceBus"]!["parameterValues"]!["fullyQualifiedNamespace"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_NAMESPACE')");
        connections["keyVault"]!["parameterValues"]!["VaultUri"]!.GetValue<string>().Should().Be("@appsetting('KEY_VAULT_URI')");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = ShipperInvoiceWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }

    [Fact]
    public void TheSubscription_AdmitsOnlyShipperInvoiceIssuedAndCreditNoteIssued_AndAResubmitToItself()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var filter = messaging[messaging.IndexOf("resource shipperInvoiceFilter", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}\n", StringComparison.Ordinal)];

        filter.Should().Contain("(eventType IN (\\'ShipperInvoiceIssued\\', \\'CreditNoteIssued\\') AND resubmitTo IS NULL) OR resubmitTo = \\'shipper-invoice\\'");
    }

    [Fact]
    public void TheLocalEmulator_GivesTheSubscriptionTheSameCondition() =>
        Contracts.Read("AppHost/ResubmitAwareFilters.cs").Should().Contain("[\"shipper-invoice\"] = \"eventType IN ('ShipperInvoiceIssued', 'CreditNoteIssued')\"");

    [Fact]
    public void TheShipperInvoiceIdentity_HoldsTheErpReportRole_AndNothingElseOnTheErp()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grants = identities.Split("\n\n").Where(block => block.StartsWith("resource ", StringComparison.Ordinal) && block.Contains("principalId: shipperInvoiceIdentity.properties.principalId", StringComparison.Ordinal)).ToList();

        grants.Should().ContainSingle();
        grants.Single().Should().Contain("resourceId: erpApiPrincipal.id").And.Contain("appRoleId: shipperInvoicesReportRoleId");
    }

    [Fact]
    public void TheShipperInvoiceIdentity_ReadsEachShipperEndpointCredentialAlone_NotTheWholeVault()
    {
        var portal = Contracts.Read("Estate/partner-api.bicep");
        var block = portal[portal.IndexOf("resource shipperInvoiceReadsTheShipperEndpointKeys", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("\n]\n", StringComparison.Ordinal)];

        block.Should().Contain("scope: shipperEndpointKeys[i]").And.Contain("principalId: shipperInvoicePrincipalId");
    }
}
