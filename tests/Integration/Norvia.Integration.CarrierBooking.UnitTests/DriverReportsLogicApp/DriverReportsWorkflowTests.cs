using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.DriverReportsLogicApp;

/// <summary>
/// The P12 Driver reports Logic App's workflow definition keeps to the tracking contract (coding guidelines §7), reads its subscription one Trip's report at a
/// time and settles each message before it tells the Driver app (ADR 0016). The Logic Apps runtime runs only in Azure, so these tests read the definition;
/// the local worker plays the same steps.
/// </summary>
public sealed class DriverReportsWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_report')?['correlationId']}";

    private static readonly string[] CalledActions =
    [
        DriverReportsWorkflow.SendToTms, DriverReportsWorkflow.ReportRecorded, DriverReportsWorkflow.ReportNotRecorded, DriverReportsWorkflow.ReportRefused,
        DriverReportsWorkflow.ReportFilesGone,
    ];

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheDriverReportsSubscriptionOnTheDriverAppEventsTopic_ReadOneMessageOfASessionAtATime()
    {
        var inputs = DriverReportsWorkflow.Trigger["inputs"]!;

        DriverReportsWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ServiceProvider");
        inputs["parameters"]!["topicName"]!.GetValue<string>().Should().Be("driver-app-events");
        inputs["parameters"]!["subscriptionName"]!.GetValue<string>().Should().Be("driver-reports");
        inputs["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("onSingleNewMessageFromTopicSession");
        inputs["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/serviceBus");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheDriversActionCorrelationId() =>
        DriverReportsWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['userProperties']?['correlationId']}");

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        DriverReportsWorkflow.Action(DriverReportsWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        DriverReportsWorkflow.Actions.Where(action => action.Key != DriverReportsWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheEventTheMessageCarries_LeavingOutAMissingOne()
    {
        var inputs = DriverReportsWorkflow.Action(DriverReportsWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo(
            "correlationId", "partyId", "tripNumber", "eventType", "sourceMessageId", "occurredAt", "version", "shipmentNumber", "stop", "trackingEventType", "location", "remark",
            "documentId", "receivedBy", "deliveryTime", "condition", "files");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['contentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['driverId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void EveryOperationThatTracks_TracksTheFlowByItsStableName()
    {
        var tracked = DriverReportsWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == DriverReportsWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain([
            DriverReportsWorkflow.Tracking, DriverReportsWorkflow.SendToTms, DriverReportsWorkflow.ReportRecorded, DriverReportsWorkflow.ReportNotRecorded,
            DriverReportsWorkflow.ReportRefused, DriverReportsWorkflow.DeadLetterUnavailable, DriverReportsWorkflow.DeadLetterRefused, DriverReportsWorkflow.DeadLetterUnreadable]);
    }

    [Fact]
    public void Check_LooksOnlyAtTheTrackedOutputs_SoAnUnreadableMessageIsDeadLetteredNotFailed()
    {
        var check = DriverReportsWorkflow.Action(DriverReportsWorkflow.Check);

        Statuses(check, DriverReportsWorkflow.InitializeFileParts).Should().BeEquivalentTo("Succeeded", "Failed");
        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("TripStartReported").And.Contain("StopCompletedReported").And.Contain("ProofOfDeliveryCaptured").And.Contain("correlationId").And.Contain("tripNumber").And.Contain("partyId").And.Contain("sourceMessageId");
        expression.Should().Contain("shipmentNumber").And.Contain("trackingEventType").And.Contain("occurredAt").And.Contain("place").And.Contain("postcode").And.Contain("countryCode");
        expression.Should().NotContain("triggerBody");
    }

    [Fact]
    public void SendToTms_PostsTheReportToTheTmsOperationItNames_AsTheDriverReportsIdentity_WithTheDriversCorrelationId()
    {
        var send = DriverReportsWorkflow.Action(DriverReportsWorkflow.SendToTms);
        var inputs = send["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        var uri = inputs["uri"]!.GetValue<string>();
        uri.Should().StartWith("@{parameters('tmsBaseUrl')}/v1/");
        uri.Should().Contain("equals(outputs('Track_the_report')?['eventType'], 'StopCompletedReported')").And.Contain("'tracking-events'");
        uri.Should().Contain("concat('trips/', outputs('Track_the_report')?['tripNumber'], '/start')");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('driverReportsIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
        uri.Should().Contain("'proofs-of-delivery'");
        Statuses(send, DriverReportsWorkflow.BuildMultipart).Should().Equal("Succeeded");
    }

    [Fact]
    public void TheBodies_AreBuiltFromTheTrackedReport_TheStartWithTheDriverIdAndEventId_TheTrackingEventAsOwnFleetWithTheDriverIdFromTheEvent()
    {
        var start = DriverReportsWorkflow.Action(DriverReportsWorkflow.BuildTripStart);
        var tracking = DriverReportsWorkflow.Action(DriverReportsWorkflow.BuildTrackingEvent);
        var body = DriverReportsWorkflow.Action(DriverReportsWorkflow.SendToTms)["inputs"]!["body"]!.GetValue<string>();

        start["runAfter"]!.AsObject().Should().BeEmpty();
        start["inputs"]!["driverId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['partyId']");
        start["inputs"]!["driverAppEventId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['sourceMessageId']");
        Statuses(tracking, DriverReportsWorkflow.BuildTripStart).Should().Equal("Succeeded");
        var inputs = tracking["inputs"]!;
        inputs["eventId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['sourceMessageId']");
        inputs["source"]!["type"]!.GetValue<string>().Should().Be("OwnFleet");
        inputs["source"]!["driverId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['partyId']");
        inputs["tripNumber"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['tripNumber']");
        inputs["shipmentNumber"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['shipmentNumber']");
        inputs["tripVersion"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['version']");
        inputs["type"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['trackingEventType']");
        inputs["eventTime"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['occurredAt']");
        inputs["location"]!["place"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['location']?['place']");
        inputs["location"]!["postcode"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['location']?['postcode']");
        inputs["location"]!["countryCode"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['location']?['countryCode']");
        inputs.AsObject().ContainsKey("remark").Should().BeFalse("a remark the Driver didn't give is left out, not sent empty");
        body.Should().Contain("addProperty(outputs('Build_the_Tracking_event'), 'remark', outputs('Track_the_report')?['remark'])")
            .And.Contain("empty(outputs('Track_the_report')?['remark'])").And.Contain("outputs('Build_the_Trip_start')");
    }

    [Theory]
    [InlineData(DriverReportsWorkflow.SendToTms)]
    [InlineData(DriverReportsWorkflow.ReportRecorded)]
    [InlineData(DriverReportsWorkflow.ReportNotRecorded)]
    [InlineData(DriverReportsWorkflow.ReportRefused)]
    [InlineData(DriverReportsWorkflow.ReportFilesGone)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AndCarryTheCorrelationId(string name)
    {
        var inputs = DriverReportsWorkflow.Action(name)["inputs"]!;
        var retry = inputs["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
    }

    [Fact]
    public void Calls_AreTheTmsAndTheOutcomeReports() =>
        DriverReportsWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(CalledActions);

    [Theory]
    [InlineData(DriverReportsWorkflow.ReportRecorded, "Recorded")]
    [InlineData(DriverReportsWorkflow.ReportNotRecorded, "NotRecorded")]
    [InlineData(DriverReportsWorkflow.ReportRefused, "Refused")]
    public void OutcomeReports_GoToTheDriverAppsOutcomeEndpoint_AsTheDriverReportsIdentity_NamingTheEventTheTripAndTheOutcome(string name, string outcome)
    {
        var inputs = DriverReportsWorkflow.Action(name)["inputs"]!;
        var body = inputs["body"]!;

        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('driverAppBaseUrl')}/v1/report-outcomes");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('driverReportsIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('driverAppAudience')");
        body["outcome"]!.GetValue<string>().Should().Be(outcome);
        body["driverAppEventId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['sourceMessageId']");
        body["tripNumber"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['tripNumber']");
        body["correlationId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['correlationId']");
    }

    [Fact]
    public void ARefusal_CarriesTheTmsCodeAndMessage_AndANotRecordedReport_TheCauseWithTheAttempts()
    {
        var refused = DriverReportsWorkflow.Action(DriverReportsWorkflow.ReportRefused)["inputs"]!["body"]!;
        var lost = DriverReportsWorkflow.Action(DriverReportsWorkflow.ReportNotRecorded)["inputs"]!["body"]!;

        refused["failureCode"]!.GetValue<string>().Should().Contain("['code']").And.Contain("TmsCallRefused");
        refused["message"]!.GetValue<string>().Should().Contain("['detail']").And.Contain("min(1000,");
        lost["failureCode"]!.GetValue<string>().Should().Be("TmsUnavailable");
        lost["message"]!.GetValue<string>().Should().Contain("retryHistory").And.Contain("didn''t answer");
    }

    [Fact]
    public void Recorded_CompletesTheMessage_ThenTellsTheDriverApp()
    {
        var settle = DriverReportsWorkflow.Action(DriverReportsWorkflow.Settle);

        Statuses(settle, DriverReportsWorkflow.SendToTms).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        settle["expression"]!.ToJsonString().Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
        settle["actions"]!.AsObject().Select(action => action.Key).Should().Equal(DriverReportsWorkflow.Complete, DriverReportsWorkflow.ReportRecorded, DriverReportsWorkflow.DeleteFiles);
        DriverReportsWorkflow.Action(DriverReportsWorkflow.Complete)["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("completeMessageInSession");
        Statuses(DriverReportsWorkflow.Action(DriverReportsWorkflow.ReportRecorded), DriverReportsWorkflow.Complete).Should().Equal("Succeeded");
    }

    [Theory]
    [InlineData(DriverReportsWorkflow.DeadLetterUnavailable, DriverReportsWorkflow.ReportNotRecorded)]
    [InlineData(DriverReportsWorkflow.DeadLetterRefused, DriverReportsWorkflow.ReportRefused)]
    public void ARefusedOrLostReport_IsDeadLetteredWithTheCodeAndMessage_BeforeTheDriverAppIsTold(string deadLetter, string report)
    {
        var inputs = DriverReportsWorkflow.Action(deadLetter)["inputs"]!;

        inputs["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("deadLetterMessageInSession");
        inputs["parameters"]!["deadLetterReason"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        inputs["parameters"]!["deadLetterErrorDescription"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        inputs["parameters"]!["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['lockToken']");
        inputs["parameters"]!["messageId"]!.GetValue<string>().Should().Be("@triggerBody()?['messageId']");
        DriverReportsWorkflow.Action(deadLetter)["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        Statuses(DriverReportsWorkflow.Action(report), deadLetter).Should().Equal("Succeeded");
    }

    [Fact]
    public void ATmsThatWasUnavailable_IsToldApartFromARefusal_ByTheStatusTheRetryPolicyRetries()
    {
        var check = DriverReportsWorkflow.Action(DriverReportsWorkflow.CheckUnavailable);

        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("408").And.Contain("429").And.Contain("500");
        check["actions"]!.AsObject().Select(action => action.Key).Should().Equal(DriverReportsWorkflow.DeadLetterUnavailable, DriverReportsWorkflow.ReportNotRecorded);
        check["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Equal(DriverReportsWorkflow.DeadLetterRefused, DriverReportsWorkflow.ReportRefused);
    }

    [Fact]
    public void UnreadableReport_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = DriverReportsWorkflow.Action(DriverReportsWorkflow.DeadLetterUnreadable);
        var parameters = deadLetter["inputs"]!["parameters"]!;

        DriverReportsWorkflow.Action(DriverReportsWorkflow.Check)["else"]!["actions"]!.AsObject().Select(action => action.Key)
            .Should().Equal(DriverReportsWorkflow.DeadLetterUnreadable, DriverReportsWorkflow.FailTheRun);
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("DriverReportUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("Trip start, a Stop report or a Proof of delivery").And.Contain("nothing was sent to the TMS");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("DriverReportUnreadable");
        DriverReportsWorkflow.Action(DriverReportsWorkflow.FailTheRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void TheTmsCallAndTheReports_TrackTheirOutcomeAndFailureCode()
    {
        DriverReportsWorkflow.Action(DriverReportsWorkflow.SendToTms)["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("TmsUnavailable").And.Contain("TmsCallRefused");
        foreach (var name in new[] { DriverReportsWorkflow.ReportRecorded, DriverReportsWorkflow.ReportNotRecorded, DriverReportsWorkflow.ReportRefused })
        {
            var tracked = DriverReportsWorkflow.Action(name)["trackedProperties"]!.AsObject();
            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "tripNumber", "correlationId", "partyId", "sourceMessageId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("DriverAppUnavailable").And.Contain("DriverAppRefused");
        }
    }

    [Fact]
    public void NoStepHidesItsBodies_BecauseNothingInTheWorkflowIsASecret() =>
        DriverReportsWorkflow.Operations().Should().OnlyContain(operation => operation.Operation["runtimeConfiguration"] == null || operation.Operation["runtimeConfiguration"]!["secureData"] == null);

    [Fact]
    public void TheSubscription_AdmitsTheTripStartAndTheStopReports_InTheEstateAndInTheLocalEmulatorAlike()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var filter = messaging[messaging.IndexOf("resource driverReportsFilter", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}\n", StringComparison.Ordinal)];
        var local = Contracts.Read("AppHost/ResubmitAwareFilters.cs");
        var localFilter = local.Split('\n').Single(line => line.Contains("[\"driver-reports\"]", StringComparison.Ordinal));

        filter.Should().Contain("eventType = \\'TripStartReported\\' OR eventType = \\'StopCompletedReported\\' OR eventType = \\'ProofOfDeliveryCaptured\\'");
        localFilter.Should().Contain("eventType = 'TripStartReported' OR eventType = 'StopCompletedReported' OR eventType = 'ProofOfDeliveryCaptured'");
    }

    [Fact]
    public void TheDriverReportsIdentity_HoldsTheTrackingEventRecordRole_NextToTheTripStartRole_ForItsStopReports()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grant = identities[identities.IndexOf("resource driverReportsRecordRoleGrant", StringComparison.Ordinal)..];
        grant = grant[..grant.IndexOf("\n}\n", StringComparison.Ordinal)];

        grant.Should().Contain("resourceId: tmsApiPrincipal.id").And.Contain("appRoleId: recordRoleId").And.Contain("principalId: driverReportsIdentity.properties.principalId");
        identities.Should().Contain("resource driverReportsStartRoleGrant");
    }

    [Fact]
    public void TheDriverReportsIdentity_HoldsTheProofOfDeliveryRecordRole_ForItsProofsOfDelivery()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grant = identities[identities.IndexOf("resource driverReportsRecordProofRoleGrant", StringComparison.Ordinal)..];
        grant = grant[..grant.IndexOf("\n}\n", StringComparison.Ordinal)];

        grant.Should().Contain("resourceId: tmsApiPrincipal.id").And.Contain("appRoleId: recordProofRoleId").And.Contain("principalId: driverReportsIdentity.properties.principalId");
    }

    [Fact]
    public void AProofOfDelivery_IsFetchedFileByFileInOrder_BeforeItsMultipartCall_AndTheFilesAreAppendedAsParts()
    {
        var fetch = DriverReportsWorkflow.Action(DriverReportsWorkflow.FetchFiles);
        var read = DriverReportsWorkflow.Action(DriverReportsWorkflow.ReadFile)["inputs"]!;
        var append = DriverReportsWorkflow.Action(DriverReportsWorkflow.AppendFilePart)["inputs"]!;

        DriverReportsWorkflow.Action(DriverReportsWorkflow.InitializeFileParts)["type"]!.GetValue<string>().Should().Be("InitializeVariable");
        Statuses(DriverReportsWorkflow.Action(DriverReportsWorkflow.InitializeFileParts), DriverReportsWorkflow.Tracking).Should().BeEquivalentTo("Succeeded", "Failed");
        fetch["foreach"]!.GetValue<string>().Should().Contain("['files']");
        fetch["runtimeConfiguration"]!["concurrency"]!["repetitions"]!.GetValue<int>().Should().Be(1, "the parts keep the order of the files");
        Statuses(fetch, DriverReportsWorkflow.BuildTrackingEvent).Should().Equal("Succeeded");
        read["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("readBlob");
        read["serviceProviderConfiguration"]!["connectionName"]!.GetValue<string>().Should().Be("AzureBlob");
        read["parameters"]!["containerName"]!.GetValue<string>().Should().Be("@parameters('driverProofsContainer')");
        read["parameters"]!["blobName"]!.GetValue<string>().Should().Contain("['blobReference']");
        append["name"]!.GetValue<string>().Should().Be("fileParts");
        append["value"]!["headers"]!["Content-Disposition"]!.GetValue<string>().Should().Contain("name=\"files\"").And.Contain("['name']");
        append["value"]!["body"]!.GetValue<string>().Should().Contain("Read_the_file");
    }

    [Fact]
    public void TheMultipartCall_HasTheMetadataOfOwnFleetThenTheFiles_AndLeavesOutARemarkTheDriverDidNotGive()
    {
        var metadata = DriverReportsWorkflow.Action(DriverReportsWorkflow.BuildProofOfDelivery)["inputs"]!;
        var multipart = DriverReportsWorkflow.Action(DriverReportsWorkflow.BuildMultipart)["inputs"]!;
        var body = DriverReportsWorkflow.Action(DriverReportsWorkflow.SendToTms)["inputs"]!["body"]!.GetValue<string>();

        metadata["source"]!["type"]!.GetValue<string>().Should().Be("OwnFleet");
        metadata["source"]!["driverId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['partyId']");
        metadata["documentId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['documentId']");
        metadata["tripNumber"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['tripNumber']");
        metadata["shipmentNumber"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['shipmentNumber']");
        metadata["tripVersion"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['version']");
        metadata["condition"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['condition']");
        metadata["files"]!.GetValue<string>().Should().Be("@body('Select_the_file_descriptions')");
        metadata.AsObject().ContainsKey("remark").Should().BeFalse();
        multipart["$content-type"]!.GetValue<string>().Should().Be("multipart/form-data");
        multipart["$multipart"]!.GetValue<string>().Should().Contain("name=\"metadata\"").And.Contain("addProperty(outputs('Build_the_Proof_of_delivery')").And.Contain("variables('fileParts')");
        body.Should().Contain("ProofOfDeliveryCaptured").And.Contain("outputs('Build_the_Proof_of_delivery_multipart')");
        DriverReportsWorkflow.Action(DriverReportsWorkflow.SendToTms)["inputs"]!["headers"]!.AsObject().ContainsKey("Content-Type")
            .Should().BeFalse("a multipart body brings its own content type with its boundary");
    }

    [Fact]
    public void FilesThatAreGone_FailTheRun_AfterTheMessageIsDeadLetteredAndTheDriverAppToldItWasNotRecorded()
    {
        var deadLetter = DriverReportsWorkflow.Action(DriverReportsWorkflow.DeadLetterFilesGone);
        var report = DriverReportsWorkflow.Action(DriverReportsWorkflow.ReportFilesGone);

        Statuses(deadLetter, DriverReportsWorkflow.FetchFiles).Should().BeEquivalentTo("Failed", "TimedOut");
        deadLetter["inputs"]!["parameters"]!["deadLetterReason"]!.GetValue<string>().Should().Be("ProofOfDeliveryFilesGone");
        deadLetter["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("deadLetterMessageInSession");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        Statuses(report, DriverReportsWorkflow.DeadLetterFilesGone).Should().Equal("Succeeded");
        report["inputs"]!["body"]!["outcome"]!.GetValue<string>().Should().Be("NotRecorded");
        report["inputs"]!["body"]!["failureCode"]!.GetValue<string>().Should().Be("ProofOfDeliveryFilesGone");
        report["inputs"]!["body"]!["driverAppEventId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['sourceMessageId']");
        DriverReportsWorkflow.Action(DriverReportsWorkflow.FailTheRunOnFilesGone)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
        Statuses(DriverReportsWorkflow.Action(DriverReportsWorkflow.SendToTms), DriverReportsWorkflow.BuildMultipart).Should().Equal("Succeeded");
    }

    [Fact]
    public void TheFiles_AreDeletedOnlyOnceTheTmsAcceptedTheProofOfDelivery_AfterTheDriverAppIsTold()
    {
        var delete = DriverReportsWorkflow.Action(DriverReportsWorkflow.DeleteFiles);
        var inputs = DriverReportsWorkflow.Action(DriverReportsWorkflow.DeleteFile)["inputs"]!;

        DriverReportsWorkflow.Action(DriverReportsWorkflow.Settle)["actions"]!.AsObject().ContainsKey(DriverReportsWorkflow.DeleteFiles).Should().BeTrue("only the accepted branch deletes");
        Statuses(delete, DriverReportsWorkflow.ReportRecorded).Should().BeEquivalentTo("Succeeded", "Failed");
        inputs["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("deleteBlob");
        inputs["parameters"]!["containerName"]!.GetValue<string>().Should().Be("@parameters('driverProofsContainer')");
        inputs["parameters"]!["blobName"]!.GetValue<string>().Should().Contain("['blobReference']");
    }

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        DriverReportsWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = DriverReportsWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "driverReportsIdentityId", "driverAppBaseUrl", "driverAppAudience", "driverProofsContainer");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connection_UsesTheDriverReportsIdentity_NotAKey()
    {
        var connections = DriverReportsWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().Equal("serviceBus", "AzureBlob");
        var authProvider = connections["serviceBus"]!["parameterValues"]!["authProvider"]!;
        authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('DRIVER_REPORTS_IDENTITY_ID')");
        connections["serviceBus"]!["parameterValues"]!["fullyQualifiedNamespace"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_NAMESPACE')");
        var blob = connections["AzureBlob"]!["parameterValues"]!;
        blob["authProvider"]!["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        blob["authProvider"]!["Identity"]!.GetValue<string>().Should().Be("@appsetting('DRIVER_REPORTS_IDENTITY_ID')");
        blob["blobStorageEndpoint"]!.GetValue<string>().Should().Be("@appsetting('DRIVER_PROOFS_BLOB_ENDPOINT')");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = DriverReportsWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }
}
