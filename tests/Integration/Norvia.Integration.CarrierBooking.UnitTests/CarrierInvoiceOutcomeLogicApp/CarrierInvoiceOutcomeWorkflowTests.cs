using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierInvoiceOutcomeLogicApp;

/// <summary>
/// The E2 Carrier invoice outcome Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and E2. The Logic Apps
/// runtime runs only in Azure, so these tests read the definition; the map it calls is tested on its own, and the local worker plays
/// the same steps (ADR 0030, ADR 0043).
/// </summary>
public sealed class CarrierInvoiceOutcomeWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_invoice')?['correlationId']}";

    /// <summary>A report goes to the Carrier invoice of this Carrier and this invoice number, both escaped for the address.</summary>
    private const string ReportPath = "@{parameters('erpBaseUrl')}/v1/carrier-invoices/@{encodeUriComponent(outputs('Track_the_invoice')?['partyId'])}/@{encodeUriComponent(outputs('Track_the_invoice')?['documentNumber'])}";

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheCarrierInvoiceOutcomeSubscriptionOnTheInvoicingEventsTopic()
    {
        var parameters = CarrierInvoiceOutcomeWorkflow.Trigger["inputs"]!["parameters"]!;

        CarrierInvoiceOutcomeWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ServiceProvider");
        CarrierInvoiceOutcomeWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("peekLockTopicMessages");
        parameters["topicName"]!.GetValue<string>().Should().Be("invoicing-events");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("carrier-invoice-outcome");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheInvoicesCorrelationId() =>
        CarrierInvoiceOutcomeWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['userProperties']?['correlationId']}");

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        CarrierInvoiceOutcomeWorkflow.Actions.Where(action => action.Key != CarrierInvoiceOutcomeWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
        Statuses(CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.MapAction), CarrierInvoiceOutcomeWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheApprovedInvoiceTheMessageCarries()
    {
        var inputs = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "documentNumber", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['contentData']))?['", StringComparison.Ordinal)
                && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["correlationId"]!.GetValue<string>().Should().EndWith("?['correlationId']");
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['carrierId']");
        inputs["documentNumber"]!.GetValue<string>().Should().EndWith("?['invoiceNumber']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "documentNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(CarrierInvoiceOutcomeWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = CarrierInvoiceOutcomeWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == CarrierInvoiceOutcomeWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain(
            [CarrierInvoiceOutcomeWorkflow.Tracking, CarrierInvoiceOutcomeWorkflow.MapAction, CarrierInvoiceOutcomeWorkflow.Send, CarrierInvoiceOutcomeWorkflow.ReportTold, CarrierInvoiceOutcomeWorkflow.ReportNotDelivered]);
    }

    [Fact]
    public void Map_RunsTheCarrierInvoiceOutcomeMapOnTheMessageContent_AndTracksAnUnreadableInvoiceAsFailed()
    {
        var map = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.MapAction);

        map["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapCarrierInvoiceOutcome");
        map["inputs"]!["parameters"]!["message"]!.GetValue<string>().Should().Be("@{triggerBody()?['contentData']}");
        map["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("CarrierInvoiceApprovedUnreadable");
    }

    [Fact]
    public void UnreadableInvoice_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = CarrierInvoiceOutcomeWorkflow.Action("Dead_letter_the_unreadable_invoice");
        var parameters = deadLetter["inputs"]!["parameters"]!;

        Statuses(deadLetter, CarrierInvoiceOutcomeWorkflow.MapAction).Should().BeEquivalentTo("Failed");
        deadLetter["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("deadLetterTopicMessageV2");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("carrier-invoice-outcome");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['lockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("CarrierInvoiceApprovedUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("CarrierInvoiceApprovedUnreadable");
        CarrierInvoiceOutcomeWorkflow.Action("Fail_the_run_on_the_unreadable_invoice")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void MappedInvoice_IsCompletedBeforeAnythingIsSent_SoAFailedRunIsNotRedelivered()
    {
        Statuses(CarrierInvoiceOutcomeWorkflow.Action("Complete_the_message"), CarrierInvoiceOutcomeWorkflow.MapAction).Should().BeEquivalentTo("Succeeded");
        Statuses(CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Credential), "Complete_the_message").Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_IsReadFromKeyVaultByTheCarriersId_AndItsOutputIsSecure()
    {
        var credential = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Credential);

        credential["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/keyVault");
        credential["inputs"]!["parameters"]!["secretName"]!.GetValue<string>().Should().Be("carrier-endpoint-key-@{outputs('Track_the_invoice')?['partyId']}");
        credential["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>()).Should().Equal("outputs");
    }

    [Fact]
    public void Send_PostsTheMappedOutcomeToTheCarriersInvoiceOutcomeEndpointInThePortal_WithTheInvoicesCorrelationIdAndTheCredential()
    {
        var send = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Send);
        var inputs = send["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('partnerPortalBaseUrl')}/carrier/v1/@{outputs('Track_the_invoice')?['partyId']}/invoice-outcomes");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["headers"]!["X-Api-Key"]!.GetValue<string>().Should().Be("@{body('Get_the_Carrier_endpoint_credential')?['value']}");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_invoice'))");
        Statuses(send, CarrierInvoiceOutcomeWorkflow.Credential).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_NeverAppearsInTheWorkflow_OnlyAsTheOutputOfTheStepThatReadsIt()
    {
        CarrierInvoiceOutcomeWorkflow.Operations().Where(operation => operation.Operation.ToJsonString().Contains(CarrierInvoiceOutcomeWorkflow.Credential, StringComparison.Ordinal))
            .Select(operation => operation.Name).Should().Equal(CarrierInvoiceOutcomeWorkflow.Send);
    }

    [Fact]
    public void SecureData_IsOnlyOnTheStepsThatHandleTheCredential()
    {
        // Anywhere else it would hide the body the Integration operator needs (coding guidelines §7).
        var secure = CarrierInvoiceOutcomeWorkflow.Operations().Where(operation => operation.Operation["runtimeConfiguration"]?["secureData"] is not null)
            .ToDictionary(operation => operation.Name, operation => operation.Operation["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Single()!.GetValue<string>());

        secure.Should().BeEquivalentTo(new Dictionary<string, string> { [CarrierInvoiceOutcomeWorkflow.Credential] = "outputs", [CarrierInvoiceOutcomeWorkflow.Send] = "inputs" });
    }

    [Theory]
    [InlineData(CarrierInvoiceOutcomeWorkflow.Send)]
    [InlineData(CarrierInvoiceOutcomeWorkflow.ReportTold)]
    [InlineData(CarrierInvoiceOutcomeWorkflow.ReportNotDelivered)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AsTheOtherLogicAppsDo(string name)
    {
        var retry = CarrierInvoiceOutcomeWorkflow.Action(name)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Theory]
    [InlineData(CarrierInvoiceOutcomeWorkflow.Send)]
    [InlineData(CarrierInvoiceOutcomeWorkflow.ReportTold)]
    [InlineData(CarrierInvoiceOutcomeWorkflow.ReportNotDelivered)]
    public void Calls_CarryTheInvoicesCorrelationId(string name) =>
        CarrierInvoiceOutcomeWorkflow.Action(name)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);

    [Fact]
    public void ReportTold_CallsTheErpsReportToldOperationAsTheCarrierInvoiceOutcomeIdentity()
    {
        var report = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.ReportTold);
        var inputs = report["inputs"]!;

        report["runAfter"]!.AsObject().Should().BeEmpty("it sits in the Report branch that runs once the Carrier acknowledged");
        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be($"{ReportPath}/report-told");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('carrierInvoiceOutcomeIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('erpAudience')");
    }

    [Fact]
    public void Report_RunsWhateverTheCarrierAnswered_AndReportsToldOnASuccess_AndNotDeliveredOtherwise()
    {
        var report = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Report);

        Statuses(report, CarrierInvoiceOutcomeWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        report["actions"]!.AsObject().Select(action => action.Key).Should().Equal(CarrierInvoiceOutcomeWorkflow.ReportTold);
        report["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Contain(CarrierInvoiceOutcomeWorkflow.ReportNotDelivered);
        var expression = report["expression"]!.ToJsonString();
        expression.Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
    }

    [Fact]
    public void ReportNotDelivered_CallsTheErpsNotDeliveredOperationAsTheCarrierInvoiceOutcomeIdentity()
    {
        var inputs = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.ReportNotDelivered)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be($"{ReportPath}/report-not-delivered");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('carrierInvoiceOutcomeIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('erpAudience')");
    }

    [Fact]
    public void ReportNotDelivered_CarriesTheStatusCodeAndAMessageOfAtMost500Characters()
    {
        var body = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.ReportNotDelivered)["inputs"]!["body"]!.AsObject();

        body.Select(property => property.Key).Should().BeEquivalentTo("statusCode", "message");
        body["statusCode"]!.GetValue<string>().Should().Contain("coalesce(actions('Send_to_the_Carrier')?['outputs']?['statusCode'], 0)");
        body["message"]!.GetValue<string>().Should().Contain("min(500,");
    }

    [Fact]
    public void ErpReports_TrackTheOutcomeAndTheFailureCodeOfTheCall()
    {
        foreach (var name in new[] { CarrierInvoiceOutcomeWorkflow.ReportTold, CarrierInvoiceOutcomeWorkflow.ReportNotDelivered })
        {
            var tracked = CarrierInvoiceOutcomeWorkflow.Action(name)["trackedProperties"]!.AsObject();

            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "documentNumber", "correlationId", "partyId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("ErpUnavailable").And.Contain("ErpCallRefused").And.Contain($"actions('{name}')");
        }
    }

    [Fact]
    public void ReportTold_TracksTheOutcomeAndTheFailureCodeOfTheCall()
    {
        var tracked = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.ReportTold)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "documentNumber", "correlationId", "partyId"]);
        tracked["failureCode"]!.GetValue<string>().Should().Contain("ErpUnavailable").And.Contain("ErpCallRefused");
    }

    [Fact]
    public void Send_TracksTheCarriersFailureAsUnavailableOrRefused()
    {
        var failureCode = CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeWorkflow.Send)["trackedProperties"]!["failureCode"]!.GetValue<string>();

        failureCode.Should().Contain("CarrierUnavailable").And.Contain("CarrierRefused");
    }

    [Fact]
    public void Calls_AreTheCarriersEndpointAndTheErpsReports_AndNothingElse() =>
        CarrierInvoiceOutcomeWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(CarrierInvoiceOutcomeWorkflow.Send, CarrierInvoiceOutcomeWorkflow.ReportTold, CarrierInvoiceOutcomeWorkflow.ReportNotDelivered);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        CarrierInvoiceOutcomeWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = CarrierInvoiceOutcomeWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("erpBaseUrl", "erpAudience", "carrierInvoiceOutcomeIdentityId", "partnerPortalBaseUrl");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_UseTheCarrierInvoiceOutcomeIdentity_NotAKey()
    {
        var connections = CarrierInvoiceOutcomeWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("serviceBus", "keyVault");
        foreach (var (_, connection) in connections)
        {
            var authProvider = connection!["parameterValues"]!["authProvider"]!;
            authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('CARRIER_INVOICE_OUTCOME_IDENTITY_ID')");
        }

        connections["serviceBus"]!["parameterValues"]!["fullyQualifiedNamespace"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_NAMESPACE')");
        connections["keyVault"]!["parameterValues"]!["VaultUri"]!.GetValue<string>().Should().Be("@appsetting('KEY_VAULT_URI')");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = CarrierInvoiceOutcomeWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }

    [Fact]
    public void TheSubscription_AdmitsOnlyCarrierInvoiceApproved_AndAResubmitToItself()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var filter = messaging[messaging.IndexOf("resource carrierInvoiceOutcomeFilter", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}\n", StringComparison.Ordinal)];

        filter.Should().Contain("(eventType = \\'CarrierInvoiceApproved\\' AND resubmitTo IS NULL) OR resubmitTo = \\'carrier-invoice-outcome\\'");
    }

    [Fact]
    public void TheLocalEmulator_GivesTheSubscriptionTheSameCondition() =>
        Contracts.Read("AppHost/ResubmitAwareFilters.cs").Should().Contain("[\"carrier-invoice-outcome\"] = \"eventType = 'CarrierInvoiceApproved'\"");

    [Fact]
    public void TheCarrierInvoiceOutcomeIdentity_HoldsTheErpReportRole_AndNothingElseOnTheErp()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grants = identities.Split("\n\n").Where(block => block.StartsWith("resource ", StringComparison.Ordinal) && block.Contains("principalId: carrierInvoiceOutcomeIdentity.properties.principalId", StringComparison.Ordinal)).ToList();

        grants.Should().ContainSingle();
        grants.Single().Should().Contain("resourceId: erpApiPrincipal.id").And.Contain("appRoleId: carrierInvoicesReportRoleId");
    }

    [Fact]
    public void TheCarrierInvoiceOutcomeIdentity_ReadsEachCarrierEndpointCredentialAlone_NotTheWholeVault()
    {
        var portal = Contracts.Read("Estate/partner-api.bicep");
        var block = portal[portal.IndexOf("resource carrierInvoiceOutcomeReadsTheCarrierEndpointKeys", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("\n]\n", StringComparison.Ordinal)];

        block.Should().Contain("scope: carrierEndpointKeys[i]").And.Contain("principalId: carrierInvoiceOutcomePrincipalId");
    }
}
