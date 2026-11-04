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

    /// <summary>The step that closes the session once <paramref name="settle"/> has settled the message.</summary>
    private static string CloseAfter(string settle) => DriverReportsWorkflow.Operations()
        .Single(operation => operation.Operation["type"]?.GetValue<string>() == "ApiConnection"
            && operation.Operation["inputs"]!["path"]!.GetValue<string>().EndsWith("/close", StringComparison.Ordinal)
            && operation.Operation["runAfter"]?[settle] is not null).Name;

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheDriverReportsSubscriptionOnTheDriverAppEventsTopic_ReadOneMessageOfASessionAtATime()
    {
        var inputs = DriverReportsWorkflow.Trigger["inputs"]!;

        DriverReportsWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        inputs["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
        inputs["path"]!.GetValue<string>().Should().Contain("'driver-app-events'").And.Contain("'driver-reports'").And.EndWith("/messages/head/peek");
        inputs["queries"]!["sessionId"]!.GetValue<string>().Should().Be("Next Available", "the subscription requires sessions");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheDriversActionCorrelationId() =>
        DriverReportsWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{coalesce(if(equals(length(string(coalesce(triggerBody(), ''))), 0), json('{}'), triggerBody())?['Properties']?['correlationId'], '00000000-0000-0000-0000-000000000000')}");

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
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
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
        expression.Should().Contain("TripStartReported").And.Contain("TripEndReported").And.Contain("StopCompletedReported").And.Contain("ProofOfDeliveryCaptured").And.Contain("correlationId").And.Contain("tripNumber").And.Contain("partyId").And.Contain("sourceMessageId");
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
        uri.Should().Contain("equals(outputs('Track_the_report')?['eventType'], 'TripEndReported')").And.Contain("concat('trips/', outputs('Track_the_report')?['tripNumber'], '/end')");
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
        var end = DriverReportsWorkflow.Action(DriverReportsWorkflow.BuildTripEnd);
        end["inputs"]!["driverId"]!.GetValue<string>().Should().Be("@outputs('Track_the_report')?['partyId']");
        end["inputs"]!.AsObject().Count.Should().Be(1, "the TMS takes the Driver ID alone: the Trip is in the route");
        Statuses(end, DriverReportsWorkflow.BuildTripStart).Should().Equal("Succeeded");
        Statuses(tracking, DriverReportsWorkflow.BuildTripEnd).Should().Equal("Succeeded");
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
            .And.Contain("empty(outputs('Track_the_report')?['remark'])").And.Contain("outputs('Build_the_Trip_start')").And.Contain("outputs('Build_the_Trip_end')");
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
        settle["actions"]!.AsObject().Select(action => action.Key).Should().Equal(DriverReportsWorkflow.Complete, CloseAfter(DriverReportsWorkflow.Complete), DriverReportsWorkflow.ReportRecorded, DriverReportsWorkflow.DeleteFiles);
        DriverReportsWorkflow.Action(DriverReportsWorkflow.Complete)["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/complete");
        DriverReportsWorkflow.Action(DriverReportsWorkflow.Complete)["inputs"]!["queries"]!["sessionId"]!.GetValue<string>().Should().Be("@triggerBody()?['SessionId']");
        Statuses(DriverReportsWorkflow.Action(DriverReportsWorkflow.ReportRecorded), CloseAfter(DriverReportsWorkflow.Complete)).Should().BeEquivalentTo("Succeeded", "Failed");
    }

    [Theory]
    [InlineData(DriverReportsWorkflow.DeadLetterUnavailable, DriverReportsWorkflow.ReportNotRecorded)]
    [InlineData(DriverReportsWorkflow.DeadLetterRefused, DriverReportsWorkflow.ReportRefused)]
    public void ARefusedOrLostReport_IsDeadLetteredWithTheCodeAndMessage_BeforeTheDriverAppIsTold(string deadLetter, string report)
    {
        var inputs = DriverReportsWorkflow.Action(deadLetter)["inputs"]!;

        inputs["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        inputs["queries"]!["deadLetterReason"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        inputs["queries"]!["deadLetterErrorDescription"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        inputs["queries"]!["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        inputs["queries"]!["sessionId"]!.GetValue<string>().Should().Be("@triggerBody()?['SessionId']");
        DriverReportsWorkflow.Action(deadLetter)["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        Statuses(DriverReportsWorkflow.Action(report), CloseAfter(deadLetter)).Should().BeEquivalentTo("Succeeded", "Failed");
    }

    [Fact]
    public void ATmsThatWasUnavailable_IsToldApartFromARefusal_ByTheStatusTheRetryPolicyRetries()
    {
        var check = DriverReportsWorkflow.Action(DriverReportsWorkflow.CheckUnavailable);

        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("408").And.Contain("429").And.Contain("500");
        check["actions"]!.AsObject().Select(action => action.Key).Should().Equal(DriverReportsWorkflow.DeadLetterUnavailable, CloseAfter(DriverReportsWorkflow.DeadLetterUnavailable), DriverReportsWorkflow.ReportNotRecorded);
        check["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Equal(DriverReportsWorkflow.DeadLetterRefused, CloseAfter(DriverReportsWorkflow.DeadLetterRefused), DriverReportsWorkflow.ReportRefused);
    }

    [Fact]
    public void UnreadableReport_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = DriverReportsWorkflow.Action(DriverReportsWorkflow.DeadLetterUnreadable);
        var parameters = deadLetter["inputs"]!["queries"]!;

        DriverReportsWorkflow.Action(DriverReportsWorkflow.Check)["else"]!["actions"]!.AsObject().Select(action => action.Key)
            .Should().Equal(DriverReportsWorkflow.DeadLetterUnreadable, CloseAfter(DriverReportsWorkflow.DeadLetterUnreadable), DriverReportsWorkflow.FailTheRun);
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("DriverReportUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("Trip start, a Trip end, a Stop report or a Proof of delivery").And.Contain("nothing was sent to the TMS");
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

        filter.Should().Contain("eventType = \\'TripStartReported\\' OR eventType = \\'TripEndReported\\' OR eventType = \\'StopCompletedReported\\' OR eventType = \\'ProofOfDeliveryCaptured\\'");
        localFilter.Should().Contain("eventType = 'TripStartReported' OR eventType = 'TripEndReported' OR eventType = 'StopCompletedReported' OR eventType = 'ProofOfDeliveryCaptured'");
    }

    [Fact]
    public void TheDriverReportsIdentity_HoldsTheTrackingEventRecordRole_NextToTheTripStartRole_ForItsStopReports()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grant = identities[identities.IndexOf("resource driverReportsRecordRoleGrant", StringComparison.Ordinal)..];
        grant = grant[..grant.IndexOf("\n}\n", StringComparison.Ordinal)];

        grant.Should().Contain("resourceId: tmsApiPrincipal.id").And.Contain("appRoleId: recordRoleId").And.Contain("principalId: driverReportsIdentity.properties.principalId");
        identities.Should().Contain("resource driverReportsStartRoleGrant");
        var end = identities[identities.IndexOf("resource driverReportsEndRoleGrant", StringComparison.Ordinal)..];
        end = end[..end.IndexOf("\n}\n", StringComparison.Ordinal)];
        end.Should().Contain("resourceId: tmsApiPrincipal.id").And.Contain("appRoleId: tripEndRoleId").And.Contain("principalId: driverReportsIdentity.properties.principalId");
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
        deadLetter["inputs"]!["queries"]!["deadLetterReason"]!.GetValue<string>().Should().Be("ProofOfDeliveryFilesGone");
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        Statuses(report, CloseAfter(DriverReportsWorkflow.DeadLetterFilesGone)).Should().BeEquivalentTo("Succeeded", "Failed");
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

        connections.Select(connection => connection.Key).Should().Equal("AzureBlob");
        var blob = connections["AzureBlob"]!["parameterValues"]!;
        blob["authProvider"]!["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        blob["authProvider"]!["Identity"]!.GetValue<string>().Should().Be("@appsetting('DRIVER_REPORTS_IDENTITY_ID')");
        blob["blobStorageEndpoint"]!.GetValue<string>().Should().Be("@appsetting('DRIVER_PROOFS_BLOB_ENDPOINT')");
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheDriverReportsIdentity_ForTheTokenStoreAndForServiceBus()
    {
        var connections = DriverReportsWorkflow.ConnectionsFile["managedApiConnections"]!.AsObject();
        var serviceBus = connections["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("servicebus");
        serviceBus["api"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_API_ID')");
        serviceBus["connection"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_ID')");
        serviceBus["connectionRuntimeUrl"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_RUNTIME_URL')");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('DRIVER_REPORTS_IDENTITY_ID')");
        }

        serviceBus["connectionProperties"]!["authentication"]!["audience"]!.GetValue<string>().Should().Be("https://servicebus.azure.net");
    }

    [Fact]
    public void EveryServiceBusStep_SettlesTheMessageOfItsSession_ByTheLockToken_OrClosesThatSession()
    {
        var steps = DriverReportsWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection" && operation.Name != "trigger").ToList();
        var settling = steps.Where(step => !step.Operation["inputs"]!["path"]!.GetValue<string>().EndsWith("/close", StringComparison.Ordinal)).ToList();

        settling.Should().HaveCount(5, "Complete and four dead letters");
        settling.Should().OnlyContain(step => step.Operation["inputs"]!["queries"]!["lockToken"]!.GetValue<string>() == "@triggerBody()?['LockToken']"
            && step.Operation["inputs"]!["queries"]!["sessionId"]!.GetValue<string>() == "@triggerBody()?['SessionId']"
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'driver-reports'", StringComparison.Ordinal));
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>() == "servicebus");
    }

    [Fact]
    public void EverySettlingStep_IsFollowedByClosingItsSession_SoTheNextMessageIsNotHeldBackUntilTheSessionLockExpires()
    {
        string[] settling = [DriverReportsWorkflow.Complete, DriverReportsWorkflow.DeadLetterUnavailable, DriverReportsWorkflow.DeadLetterRefused, DriverReportsWorkflow.DeadLetterUnreadable, DriverReportsWorkflow.DeadLetterFilesGone];

        foreach (var settle in settling)
        {
            var close = DriverReportsWorkflow.Action(CloseAfter(settle));

            close["inputs"]!["method"]!.GetValue<string>().Should().Be("delete");
            close["inputs"]!["path"]!.GetValue<string>().Should().Contain("'driver-app-events'").And.Contain("'driver-reports'").And.Contain("triggerBody()?['SessionId']");
            Statuses(close, settle).Should().Equal("Succeeded");
        }
    }

    [Fact]
    public void Check_TestsTheNumericVersionForNull_NotForEmpty_BecauseEmptyRefusesAnInteger()
    {
        // empty() takes a string, an array or an object; the Driver app's version is a number, and the run fails with InvalidTemplate on it.
        var expression = DriverReportsWorkflow.Action(DriverReportsWorkflow.Check)["expression"]!["equals"]![0]!.GetValue<string>();

        expression.Should().NotContain("empty(outputs('Track_the_report')?['version'])");
        expression.Should().Contain("equals(outputs('Track_the_report')?['version'], null)");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = DriverReportsWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }
}
