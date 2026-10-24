using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Norvia.DriverApp.Api.IntegrationTests;

/// <summary>
/// The Driver app's end of the reporting path (P12, ADR 0016) against its real database, outbox and Service Bus: the Driver presses Start Trip and the Driver app
/// publishes exactly one <c>TripStartReported</c> carrying the Driver ID from the token; the Driver reports Logic App tells it what became of the report, and
/// only the identity holding the outcome role may. The tests play the Logic App themselves.
/// </summary>
[Collection("Driver app")]
public sealed partial class ReportingPathTests(DriverAppFixture driverApp)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // The outbox looks for a message to publish every second; this is long enough for a second message to show up if one wrongly were published.
    private static readonly TimeSpan OutboxSettleTime = TimeSpan.FromSeconds(3);

    private const string Anouk = "DRV-0001";
    private const string Joost = "DRV-0002";

    [Fact]
    public async Task PressingStartTrip_PublishesExactlyOneTripStartReported_WithTheDriverIdFromTheToken_AndTheTripScreenShowsSending()
    {
        const string trip = "TR-3000001";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);

        using var started = await StartAsync(browser, trip);
        var message = await driverApp.FirstPublishedForAsync(trip, Ct);
        await Task.Delay(OutboxSettleTime, Ct);

        started.StatusCode.Should().Be(HttpStatusCode.Redirect);
        driverApp.PublishedFor(trip).Should().ContainSingle("exactly one event is published for one press of Start Trip");
        var body = JsonNode.Parse(message.Body.ToString())!;
        Validate("trip-start-reported.v1.schema.json", body).IsValid.Should().BeTrue(message.Body.ToString());
        body["eventType"]!.GetValue<string>().Should().Be("TripStartReported");
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["driverId"]!.GetValue<string>().Should().Be(Anouk, "the Driver ID comes from the Driver's token");
        message.MessageId.Should().Be(body["eventId"]!.GetValue<string>());
        message.SessionId.Should().Be(trip, "one Trip's reports are read in order, in a session of the Trip");
        message.CorrelationId.Should().Be(body["correlationId"]!.GetValue<string>());
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "TripStartReported",
            ["flow"] = "trip-execution",
            ["tripNumber"] = trip,
            ["partyId"] = Anouk,
            ["correlationId"] = body["correlationId"]!.GetValue<string>(),
            ["sourceMessageId"] = body["eventId"]!.GetValue<string>(),
        });
        var page = await PageAsync(browser);
        TripCardOf(page, trip).Should().Contain("Sending");
    }

    [Fact]
    public async Task PressingStartTripTwice_PublishesOnlyOnce()
    {
        const string trip = "TR-3000002";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);

        using var first = await StartAsync(browser, trip);
        using var second = await StartAsync(browser, trip);
        await driverApp.FirstPublishedForAsync(trip, Ct);
        await Task.Delay(OutboxSettleTime, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        second.StatusCode.Should().Be(HttpStatusCode.Redirect);
        driverApp.PublishedFor(trip).Should().ContainSingle();
    }

    [Fact]
    public async Task StartingATripOfAnotherDriver_IsNotFound_AndPublishesNothing()
    {
        const string trip = "TR-3000003";
        await ReceiveTripAsync(trip, Anouk);

        // Joost's own Trip gives his Trips page the form whose antiforgery token he posts back.
        await ReceiveTripAsync("TR-3000103", Joost);
        using var joost = await SignedInAsync(Joost);

        using var started = await StartAsync(joost, trip);
        await Task.Delay(OutboxSettleTime, Ct);

        started.StatusCode.Should().Be(HttpStatusCode.NotFound);
        driverApp.PublishedFor(trip).Should().BeEmpty();
    }

    [Fact]
    public async Task ARecordedOutcome_MakesTheReportRecordedAndTheTripInProgress_AndAnotherOutcomeIsIgnored()
    {
        const string trip = "TR-3000004";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var sent = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;

        using var recorded = await ReportOutcomeAsync(Outcome(sent, "Recorded"));
        using var later = await ReportOutcomeAsync(Outcome(sent, "Refused", "NotTripDay", "Too late to matter."));

        recorded.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(recorded))["result"]!.GetValue<string>().Should().Be("Applied");
        (await Json(later))["result"]!.GetValue<string>().Should().Be("Ignored");
        var card = TripCardOf(await PageAsync(browser), trip);
        card.Should().Contain("Recorded").And.Contain("In progress").And.NotContain("Sending").And.NotContain("NotTripDay");
    }

    [Fact]
    public async Task ARefusedOutcome_ShowsTheTmsCodeAndMessage_OnTheTripScreen()
    {
        const string trip = "TR-3000005";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var sent = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;

        using var refused = await ReportOutcomeAsync(Outcome(sent, "Refused", "NotTripDay", "Trip TR-3000005 is for another day."));

        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = TripCardOf(await PageAsync(browser), trip);
        card.Should().Contain("Refused").And.Contain("NotTripDay").And.Contain("Trip TR-3000005 is for another day.").And.NotContain("Sending");
    }

    [Fact]
    public async Task ANotRecordedOutcome_ShowsNotRecorded_WithTheCause()
    {
        const string trip = "TR-3000006";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var sent = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;

        using var lost = await ReportOutcomeAsync(Outcome(sent, "NotRecorded", "TmsUnavailable", "The TMS answered 503 (4 attempts)."));

        lost.StatusCode.Should().Be(HttpStatusCode.OK);
        TripCardOf(await PageAsync(browser), trip).Should().Contain("Not recorded").And.Contain("The TMS answered 503 (4 attempts).");
    }

    [Fact]
    public async Task WithoutAnOutcome_TheReportStaysSending()
    {
        const string trip = "TR-3000007";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        await driverApp.FirstPublishedForAsync(trip, Ct);

        TripCardOf(await PageAsync(browser), trip).Should().Contain("Sending");
    }

    [Fact]
    public async Task TheOutcomeEndpoint_RefusesACallerWithoutTheOutcomeRole_AndChangesNothing()
    {
        const string trip = "TR-3000008";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var outcome = Outcome(JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!, "Recorded");

        using var anonymous = driverApp.DriverReportsClient(token: null);
        using var otherRole = driverApp.DriverReportsClient(driverApp.IssueToken(["Trips.Start"]));
        using var otherAudience = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ReportOutcomeRole], audience: "api://norvia-tms"));
        using var otherKey = driverApp.DriverReportsClient(driverApp.IssueToken([DriverAppFixture.ReportOutcomeRole], signingKey: new string('a', 64)));
        using var withKeyOnly = driverApp.DriverReportsClient(token: null);
        withKeyOnly.DefaultRequestHeaders.Add("X-Api-Key", DriverAppFixture.EndpointKey);

        (await anonymous.PostAsJsonAsync("/v1/report-outcomes", outcome, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await otherRole.PostAsJsonAsync("/v1/report-outcomes", outcome, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await otherAudience.PostAsJsonAsync("/v1/report-outcomes", outcome, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await otherKey.PostAsJsonAsync("/v1/report-outcomes", outcome, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await withKeyOnly.PostAsJsonAsync("/v1/report-outcomes", outcome, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Trip endpoint's credential is not an identity");
        TripCardOf(await PageAsync(browser), trip).Should().Contain("Sending");
    }

    [Fact]
    public async Task AnOutcomeForAnUnknownReport_OrForAnotherTripsReport_IsNotFound()
    {
        const string trip = "TR-3000009";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var sent = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;

        var unknown = Outcome(sent, "Recorded");
        unknown["driverAppEventId"] = Guid.NewGuid().ToString();
        var otherTrip = Outcome(sent, "Recorded");
        otherTrip["tripNumber"] = "TR-3999999";

        using var unknownAnswer = await ReportOutcomeAsync(unknown);
        using var otherTripAnswer = await ReportOutcomeAsync(otherTrip);

        unknownAnswer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(unknownAnswer))["code"]!.GetValue<string>().Should().Be("ReportNotFound");
        otherTripAnswer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        TripCardOf(await PageAsync(browser), trip).Should().Contain("Sending");
    }

    [Fact]
    public async Task AnOutcomeThatIsNotWellFormed_IsABadRequest_AndChangesNothing()
    {
        const string trip = "TR-3000010";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var sent = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;

        using var withoutCode = await ReportOutcomeAsync(Outcome(sent, "Refused", message: "No code."));
        using var withoutMessage = await ReportOutcomeAsync(Outcome(sent, "Refused", "NotTripDay"));
        var lost = Outcome(sent, "Lost");
        using var unknownOutcome = await ReportOutcomeAsync(lost);
        var blank = Outcome(sent, "Recorded");
        blank["correlationId"] = " ";
        using var blankCorrelation = await ReportOutcomeAsync(blank);

        foreach (var response in new[] { withoutCode, withoutMessage, unknownOutcome, blankCorrelation })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        TripCardOf(await PageAsync(browser), trip).Should().Contain("Sending");
    }

    private const string Collection = "SH-0000012";

    [Fact]
    public async Task ReportingCollected_PublishesExactlyOneStopCompletedReported_AfterTheStart_InTheTripsSession_WithTheDriverIdFromTheToken()
    {
        const string trip = "TR-3000011";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));

        using var collected = await PostStopAsync(browser, trip, "collection", "collected");
        await WaitForPublishedAsync(trip, 2);
        await Task.Delay(OutboxSettleTime, Ct);

        started.StatusCode.Should().Be(HttpStatusCode.OK);
        collected.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = driverApp.PublishedFor(trip);
        published.Should().HaveCount(2, "the Start Trip, then exactly one Stop report");
        var message = published[1];
        var body = JsonNode.Parse(message.Body.ToString())!;
        Validate("stop-completed-reported.v1.schema.json", body).IsValid.Should().BeTrue(message.Body.ToString());
        body["eventType"]!.GetValue<string>().Should().Be("StopCompletedReported");
        body["driverId"]!.GetValue<string>().Should().Be(Anouk, "the Driver ID comes from the Driver's token");
        body["shipmentNumber"]!.GetValue<string>().Should().Be(Collection);
        body["stop"]!.GetValue<string>().Should().Be("Collection");
        body["trackingEventType"]!.GetValue<string>().Should().Be("Collected");
        body["location"]!["place"]!.GetValue<string>().Should().Be("Rotterdam");
        body["location"]!["countryCode"]!.GetValue<string>().Should().Be("NL");
        body["correlationId"]!.GetValue<string>().Should().NotBe(start["correlationId"]!.GetValue<string>(), "each Driver action has a correlation ID of its own");
        message.MessageId.Should().Be(body["eventId"]!.GetValue<string>());
        message.SessionId.Should().Be(trip, "the Stop report is read in order after the Start, in the session of the Trip");
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "StopCompletedReported",
            ["flow"] = "trip-execution",
            ["tripNumber"] = trip,
            ["partyId"] = Anouk,
            ["sourceMessageId"] = body["eventId"]!.GetValue<string>(),
        });
        TripCardOf(await PageAsync(browser), trip).Should().Contain($"Collected {Collection}").And.Contain("Sending");
    }

    [Fact]
    public async Task ReportingTheSameStopTwice_PublishesOnlyOnce_AndADeliveryWaitsForItsCollectionBeingReported()
    {
        const string trip = "TR-3000012";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));

        using var early = await PostStopAsync(browser, trip, "delivery", "delivery-failed", "Dock closed");
        using var first = await PostStopAsync(browser, trip, "collection", "collected");
        using var second = await PostStopAsync(browser, trip, "collection", "collected");
        using var delivery = await PostStopAsync(browser, trip, "delivery", "delivery-failed", "Dock closed");
        await WaitForPublishedAsync(trip, 3);
        await Task.Delay(OutboxSettleTime, Ct);

        early.StatusCode.Should().Be(HttpStatusCode.Redirect);
        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        second.StatusCode.Should().Be(HttpStatusCode.Redirect);
        delivery.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var types = driverApp.PublishedFor(trip).Skip(1).Select(message => JsonNode.Parse(message.Body.ToString())!["trackingEventType"]!.GetValue<string>());
        types.Should().Equal("Collected", "DeliveryFailed");
    }

    [Fact]
    public async Task EndingTheTrip_PublishesExactlyOneTripEndReported_OnlyOnceEveryStopHasAnOutcome_InTheTripsSession_WithTheDriverIdFromTheToken()
    {
        const string trip = "TR-3000014";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));
        using var collected = await PostStopAsync(browser, trip, "collection", "collected");

        using var tooEarly = await EndAsync(browser, trip);
        using var delivery = await PostStopAsync(browser, trip, "delivery", "delivery-failed", "Dock closed");
        using var ended = await EndAsync(browser, trip);
        using var again = await EndAsync(browser, trip);
        await WaitForPublishedAsync(trip, 4);
        await Task.Delay(OutboxSettleTime, Ct);

        tooEarly.StatusCode.Should().Be(HttpStatusCode.Redirect);
        ended.StatusCode.Should().Be(HttpStatusCode.Redirect);
        again.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = driverApp.PublishedFor(trip);
        published.Should().HaveCount(4, "the Start Trip, the Collected, the Delivery failed, then exactly one End Trip: the first press had a Stop open");
        var message = published[3];
        var body = JsonNode.Parse(message.Body.ToString())!;
        Validate("trip-end-reported.v1.schema.json", body).IsValid.Should().BeTrue(message.Body.ToString());
        body["eventType"]!.GetValue<string>().Should().Be("TripEndReported");
        body["tripNumber"]!.GetValue<string>().Should().Be(trip);
        body["driverId"]!.GetValue<string>().Should().Be(Anouk, "the Driver ID comes from the Driver's token");
        message.SessionId.Should().Be(trip, "the End Trip is read in order after the last Stop report, in the session of the Trip");
        message.ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "TripEndReported",
            ["flow"] = "trip-execution",
            ["tripNumber"] = trip,
            ["partyId"] = Anouk,
            ["sourceMessageId"] = body["eventId"]!.GetValue<string>(),
        });
        TripCardOf(await PageAsync(browser), trip).Should().Contain("End Trip").And.Contain("Sending");
    }

    [Fact]
    public async Task ARecordedEndOutcome_MakesTheTripCompleted_AndARefusalShowsStopsOpen()
    {
        const string trip = "TR-3000015";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));
        using var collected = await PostStopAsync(browser, trip, "collection", "collected");
        using var delivery = await PostStopAsync(browser, trip, "delivery", "delivery-failed", "Dock closed");
        using var first = await EndAsync(browser, trip);
        await WaitForPublishedAsync(trip, 4);
        var end = JsonNode.Parse(driverApp.PublishedFor(trip)[3].Body.ToString())!;

        using var refused = await ReportOutcomeAsync(Outcome(end, "Refused", "StopsOpen", $"Trip {trip} still has open Stops."));
        var afterRefusal = TripCardOf(await PageAsync(browser), trip);
        using var second = await EndAsync(browser, trip);
        await WaitForPublishedAsync(trip, 5);
        var again = JsonNode.Parse(driverApp.PublishedFor(trip)[4].Body.ToString())!;
        using var recorded = await ReportOutcomeAsync(Outcome(again, "Recorded"));

        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        recorded.StatusCode.Should().Be(HttpStatusCode.OK);
        afterRefusal.Should().Contain("Refused").And.Contain("StopsOpen");
        TripCardOf(await PageAsync(browser), trip).Should().Contain("Completed");
    }

    [Fact]
    public async Task AStopOutcomeFromTheLogicApp_ShowsOnTheStop_AndARefusalShowsTheTmsCodeAndMessage()
    {
        const string trip = "TR-3000013";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));
        using var collected = await PostStopAsync(browser, trip, "collection", "collected");
        await WaitForPublishedAsync(trip, 2);
        var sent = JsonNode.Parse(driverApp.PublishedFor(trip)[1].Body.ToString())!;

        using var refused = await ReportOutcomeAsync(Outcome(sent, "Refused", "ShipmentNotOnTrip", $"Shipment {Collection} is not on Trip {trip}."));

        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        var stop = await PageAsync(browser, $"/trips/{trip}/stops/{Collection}/collection");
        stop.Should().Contain("Refused").And.Contain("ShipmentNotOnTrip").And.Contain($"Shipment {Collection} is not on Trip {trip}.").And.Contain("/collected", "a refused report can be sent again");
    }

    [Fact]
    public async Task ANewTripVersionWithoutTheShipment_KeepsTheCompletedStop_MarkedTakenOffByThePlanner()
    {
        const string trip = "TR-3000014";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));
        using var collected = await PostStopAsync(browser, trip, "collection", "collected");
        await WaitForPublishedAsync(trip, 2);
        var sent = JsonNode.Parse(driverApp.PublishedFor(trip)[1].Body.ToString())!;
        using var recorded = await ReportOutcomeAsync(Outcome(sent, "Recorded"));

        using var newVersion = await driverApp.DriverApp.PostAsync(
            "/v1/trips", new StringContent(await DriverAppFixture.SampleAsync("without-stops", trip, version: 2, Anouk), System.Text.Encoding.UTF8, "application/json"), Ct);

        newVersion.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = TripCardOf(await PageAsync(browser), trip);
        card.Should().Contain("Version 2").And.Contain("Taken off by the Planner, call the Planner").And.Contain(Collection).And.Contain("Recorded");
    }

    [Fact]
    public async Task Delivering_PublishesTheDeliveredAndTheProofOfDeliveryUnderOneCorrelationId_AndStoresTheFilesInTheTransitContainer()
    {
        const string trip = "TR-3000015";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));
        using var collected = await PostStopAsync(browser, trip, "collection", "collected");

        using var delivered = await DeliverAsync(browser, trip);
        await WaitForPublishedAsync(trip, 4);
        await Task.Delay(OutboxSettleTime, Ct);

        delivered.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var published = driverApp.PublishedFor(trip);
        published.Should().HaveCount(4, "the Start Trip, the Collected, then exactly one Delivered and one Proof of delivery");
        var stop = JsonNode.Parse(published[2].Body.ToString())!;
        var proof = JsonNode.Parse(published[3].Body.ToString())!;
        Validate("stop-completed-reported.v1.schema.json", stop).IsValid.Should().BeTrue(published[2].Body.ToString());
        Validate("proof-of-delivery-captured.v1.schema.json", proof).IsValid.Should().BeTrue(published[3].Body.ToString());
        stop["trackingEventType"]!.GetValue<string>().Should().Be("Delivered");
        proof["eventType"]!.GetValue<string>().Should().Be("ProofOfDeliveryCaptured");
        proof["correlationId"]!.GetValue<string>().Should().Be(stop["correlationId"]!.GetValue<string>(), "the Delivered and its Proof of delivery are one Driver action");
        published[3].CorrelationId.Should().Be(stop["correlationId"]!.GetValue<string>());
        published[3].SessionId.Should().Be(trip, "the Proof of delivery is read in order after the Delivered, in the session of the Trip");
        published[3].ApplicationProperties.Should().Contain(new Dictionary<string, object>
        {
            ["eventType"] = "ProofOfDeliveryCaptured",
            ["flow"] = "trip-execution",
            ["tripNumber"] = trip,
            ["partyId"] = Anouk,
            ["sourceMessageId"] = proof["eventId"]!.GetValue<string>(),
        });
        proof["driverId"]!.GetValue<string>().Should().Be(Anouk, "the Driver ID comes from the Driver's token");
        proof["files"]!.AsArray().Select(file => file!["name"]!.GetValue<string>()).Should().Equal("signature.png", "photo-1.jpg");
        var documentId = proof["documentId"]!.GetValue<string>();
        (await driverApp.TransitBlobsAsync(documentId, Ct)).Should().BeEquivalentTo(proof["files"]!.AsArray().Select(file => file!["blobReference"]!.GetValue<string>()));
        var card = TripCardOf(await PageAsync(browser), trip);
        card.Should().Contain($"Delivered {Collection}").And.Contain("Proof of delivery").And.Contain("Sending");
    }

    [Fact]
    public async Task AProofOfDeliveryTheTmsRefused_ShowsRefusedOnTheStop_WhileTheDeliveredStaysRecorded()
    {
        const string trip = "TR-3000016";
        await ReceiveTripAsync(trip, Anouk);
        using var browser = await SignedInAsync(Anouk);
        await StartAsync(browser, trip);
        var start = JsonNode.Parse((await driverApp.FirstPublishedForAsync(trip, Ct)).Body.ToString())!;
        using var started = await ReportOutcomeAsync(Outcome(start, "Recorded"));
        using var collected = await PostStopAsync(browser, trip, "collection", "collected");
        using var delivered = await DeliverAsync(browser, trip);
        await WaitForPublishedAsync(trip, 4);
        var stop = JsonNode.Parse(driverApp.PublishedFor(trip)[2].Body.ToString())!;
        var proof = JsonNode.Parse(driverApp.PublishedFor(trip)[3].Body.ToString())!;

        using var recorded = await ReportOutcomeAsync(Outcome(stop, "Recorded"));
        using var refused = await ReportOutcomeAsync(Outcome(proof, "Refused", "FileNotAccepted", "File photo-1.jpg isn't a PDF, JPEG or PNG."));

        recorded.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await PageAsync(browser, $"/trips/{trip}/stops/{Collection}/delivery");
        page.Should().Contain("This Stop is done.").And.Contain("FileNotAccepted").And.Contain("File photo-1.jpg isn&#x27;t a PDF, JPEG or PNG.");
    }

    private async Task WaitForPublishedAsync(string trip, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (driverApp.PublishedFor(trip).Count < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }

    /// <summary>Reports on the Stop of the Trip's only Shipment as the browser would, with an antiforgery token.</summary>
    private static async Task<HttpResponseMessage> PostStopAsync(HttpClient browser, string trip, string kind, string action, string? remark = null)
    {
        var token = await TokenOfAsync(browser);
        List<KeyValuePair<string, string>> form = [new("__RequestVerificationToken", token)];
        if (remark is not null)
        {
            form.Add(new("remark", remark));
        }

        return await browser.PostAsync($"/trips/{trip}/stops/{Collection}/{kind}/{action}", new FormUrlEncodedContent(form), Ct);
    }

    private async Task ReceiveTripAsync(string trip, string driverId)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")).DateTime);
        using var response = await driverApp.DriverApp.PostAsync(
            "/v1/trips",
            new StringContent(await DriverAppFixture.SampleAsync("two-shipments", trip, version: 1, driverId, sample => sample["day"] = $"{today:yyyy-MM-dd}"), System.Text.Encoding.UTF8, "application/json"),
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> SignedInAsync(string driverId)
    {
        var browser = driverApp.NewBrowser();
        using var signedIn = await browser.PostAsync("/local/sign-in", new FormUrlEncodedContent([new("driver", driverId)]), Ct);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return browser;
    }

    private static async Task<string> PageAsync(HttpClient browser, string path = "/")
    {
        using var response = await browser.GetAsync(path, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    /// <summary>The antiforgery token a form on the Trips page carries. It belongs to the browser, not to the form, so it serves any report the browser makes.</summary>
    private static async Task<string> TokenOfAsync(HttpClient browser) =>
        AntiforgeryToken().Match(await PageAsync(browser)) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("The Trips page has no form with an antiforgery token.");

    /// <summary>Presses Start Trip as the browser would: with an antiforgery token.</summary>
    private static async Task<HttpResponseMessage> StartAsync(HttpClient browser, string trip)
    {
        var token = await TokenOfAsync(browser);
        return await browser.PostAsync($"/trips/{trip}/start", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]), Ct);
    }

    /// <summary>Presses End Trip as the browser would: with an antiforgery token.</summary>
    private static async Task<HttpResponseMessage> EndAsync(HttpClient browser, string trip)
    {
        var token = await TokenOfAsync(browser);
        return await browser.PostAsync($"/trips/{trip}/end", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]), Ct);
    }

    private Task<HttpResponseMessage> ReportOutcomeAsync(JsonObject outcome) => driverApp.DriverReports.PostAsJsonAsync("/v1/report-outcomes", outcome, Ct);

    /// <summary>The outcome the Driver reports Logic App reports for the event the Driver app published, in the Canonical Model's shape.</summary>
    private static JsonObject Outcome(JsonNode sent, string outcome, string? failureCode = null, string? message = null)
    {
        var report = new JsonObject
        {
            ["driverAppEventId"] = sent["eventId"]!.GetValue<string>(),
            ["tripNumber"] = sent["tripNumber"]!.GetValue<string>(),
            ["correlationId"] = sent["correlationId"]!.GetValue<string>(),
            ["outcome"] = outcome,
        };
        if (failureCode is not null)
        {
            report["failureCode"] = failureCode;
        }

        if (message is not null)
        {
            report["message"] = message;
        }

        return report;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

    /// <summary>The card of one Trip on My Trips, so a test reads that Trip's text and not another's.</summary>
    private static string TripCardOf(string page, string trip)
    {
        var start = page.IndexOf($"aria-label=\"Trip {trip}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"My Trips shows Trip {trip}");
        var next = page.IndexOf("aria-label=\"Trip TR-", start + 1, StringComparison.Ordinal);
        return page[start..(next < 0 ? page.Length : next)];
    }

    /// <summary>Delivers the Trip's only Shipment as the browser would: the Proof of delivery form with a drawn signature and a photo, and an antiforgery token.</summary>
    private static async Task<HttpResponseMessage> DeliverAsync(HttpClient browser, string trip)
    {
        var token = await TokenOfAsync(browser);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("J. Bakker"), "receivedBy" },
            { new StringContent("clean"), "condition" },
            { new StringContent("data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3])), "signature" },
        };
        var photo = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6]);
        photo.Headers.ContentType = new("image/jpeg");
        form.Add(photo, "photos", "camera.jpg");
        return await browser.PostAsync($"/trips/{trip}/stops/{Collection}/delivery/deliver", form, Ct);
    }

    // Loading a schema registers it by its id in a registry shared by the whole process, which refuses a second registration. Tests run in
    // parallel, so each schema is loaded once.
    private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> Schemas = new();

    private static EvaluationResults Validate(string schemaFile, JsonNode body)
    {
        var schema = Schemas.GetOrAdd(schemaFile, file => new Lazy<JsonSchema>(() => JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Contracts", file)))).Value;
        return schema.Evaluate(JsonSerializer.SerializeToElement(body), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AntiforgeryToken();
}
