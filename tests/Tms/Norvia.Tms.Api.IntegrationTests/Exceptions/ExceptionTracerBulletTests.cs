using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Planning;
using Norvia.Tms.Api.IntegrationTests.ProofsOfDelivery;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;
using static Norvia.Tms.Api.IntegrationTests.Exceptions.ExceptionCalls;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>
/// A Delivery failed Tracking event (<c>NDL</c>), or any other exception event or Proof of delivery with reservations, opens an Exception in the same transaction as the event and publishes
/// ExceptionOpened (P7). The Planner sees it in the list of Open Exceptions, oldest first, and resolves it with an outcome
/// and a required remark, which publishes ExceptionResolved and takes it off the list.
/// </summary>
public sealed class ExceptionTracerBulletTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeliveryFailed_OpensOneDeliveryFailedException_AndPublishesExactlyOneExceptionOpened_OnTheCausesTrail()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.ReportDeliveryFailedAsync(reference, _cancellationToken, correlationId, eventId: "evt-ndl-1");

        var message = (await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject;
        var body = BodyOf(message);
        var results = CanonicalModelSchemas.ValidateExceptionOpened(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["exceptionNumber"]!.GetValue<string>().Should().MatchRegex("^EX-[0-9]{7}$");
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["transportOrderNumber"]!.GetValue<string>().Should().MatchRegex("^TO-[0-9]{7}$");
        body["carrierId"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        body["exceptionType"]!.GetValue<string>().Should().Be("DeliveryFailed");
        body["cause"]!["kind"]!.GetValue<string>().Should().Be("TrackingEvent");
        body["cause"]!["sourceEventId"]!.GetValue<string>().Should().Be("evt-ndl-1");
        body["afterDelivered"]!.GetValue<bool>().Should().BeFalse();
        body["correlationId"]!.GetValue<string>().Should().Be(correlationId, "the Exception carries its cause's correlation ID");
        message.CorrelationId.Should().Be(correlationId);
        message.ApplicationProperties.Should().Contain("shipmentNumber", shipmentNumber).And.Contain("assignedTo", "Carrier");
        tms.Published.Count(m => m.Subject == "ExceptionOpened" && JsonNode.Parse(m.Body.ToString())!["shipmentNumber"]!.GetValue<string>() == shipmentNumber)
            .Should().Be(1);
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Booked", "an Exception never moves the Shipment's status");
    }

    [Fact]
    public async Task OpenExceptions_ListsTheException_WithItsNumberShipmentShipperCarrierTypeOpenedAtAndCauses()
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);

        var open = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;

        open["exceptionNumber"]!.GetValue<string>().Should().MatchRegex("^EX-[0-9]{7}$");
        open["shipper"]!["id"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        open["carriedBy"]!["assignedTo"]!.GetValue<string>().Should().Be("Carrier");
        open["carriedBy"]!["carrier"]!["id"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        open["type"]!.GetValue<string>().Should().Be("DeliveryFailed");
        open["openedAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        open["numberOfCauses"]!.GetValue<int>().Should().Be(1);
        open["afterDelivered"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task DeliveryFailed_AgainWhileTheExceptionIsOpen_AddsACause_AndPublishesNoSecondExceptionOpened()
    {
        var (shipmentNumber, reference, correlationId) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken);
        var secondCorrelationId = TmsCalls.NewCorrelationId();

        await tms.ReportDeliveryFailedAsync(reference, _cancellationToken, secondCorrelationId);

        var open = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle("a Shipment has at most one open Exception of each type").Subject;
        open["numberOfCauses"]!.GetValue<int>().Should().Be(2);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == secondCorrelationId).Select(m => m.Subject).Should()
            .Equal(["ShipmentExceptionReported"], "a cause added to an open Exception publishes no ExceptionOpened");
    }

    [Fact]
    public async Task DeliveryFailed_SentTwiceUnderOneEventId_OpensOneException()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);

        await tms.ReportDeliveryFailedAsync(reference, _cancellationToken, eventId: "evt-ndl-repeat");
        await tms.ReportDeliveryFailedAsync(reference, _cancellationToken, eventId: "evt-ndl-repeat");

        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Which["numberOfCauses"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task OpenExceptions_AreListedOldestFirst()
    {
        var (first, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var (second, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);

        var listed = (await tms.ListOpenExceptionsAsync(_cancellationToken)).Select(open => open!["shipmentNumber"]!.GetValue<string>()).ToList();

        listed.IndexOf(first).Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(listed.IndexOf(second));
    }

    [Fact]
    public async Task Resolve_WithAnOutcomeAndARemark_PublishesExceptionResolved_AndRemovesItFromTheList()
    {
        var (shipmentNumber, _, openingCorrelationId) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();

        var resolved = await tms.ResolvedAsync(exceptionNumber, Resolution("ReturnedToShipper", "Goods go back to the Shipper"), _cancellationToken);

        resolved["exceptionNumber"]!.GetValue<string>().Should().Be(exceptionNumber);
        resolved["status"]!.GetValue<string>().Should().Be("Resolved");
        resolved["outcome"]!.GetValue<string>().Should().Be("ReturnedToShipper");
        var resolutionCorrelationId = resolved["correlationId"]!.GetValue<string>();
        resolutionCorrelationId.Should().NotBe(openingCorrelationId, "each resolution gets a new correlation ID from the TMS");
        var message = (await tms.PublishedAsync(resolutionCorrelationId, "ExceptionResolved", _cancellationToken)).Should().ContainSingle().Subject;
        var body = BodyOf(message);
        var results = CanonicalModelSchemas.ValidateExceptionResolved(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["exceptionNumber"]!.GetValue<string>().Should().Be(exceptionNumber);
        body["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        body["exceptionType"]!.GetValue<string>().Should().Be("DeliveryFailed");
        body["outcome"]!.GetValue<string>().Should().Be("ReturnedToShipper");
        body["remark"]!.GetValue<string>().Should().Be("Goods go back to the Shipper");
        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_WithoutARemark_IsRefused_AndLeavesTheExceptionOpen()
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();

        await tms.ResolveRefusedAsync(exceptionNumber, Resolution(remark: null), HttpStatusCode.BadRequest, _cancellationToken);
        await tms.ResolveRefusedAsync(exceptionNumber, Resolution(remark: "  "), HttpStatusCode.BadRequest, _cancellationToken);

        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Escalated")]
    [InlineData("redelivered")]
    [InlineData("1")]
    [InlineData(null)]
    public async Task Resolve_WithAnOutcomeOutsideTheList_IsRefused_AndLeavesTheExceptionOpen(string? outcome)
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();

        await tms.ResolveRefusedAsync(exceptionNumber, Resolution(outcome), HttpStatusCode.BadRequest, _cancellationToken);

        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task Resolve_AnUnknownException_IsRefused_WithExceptionNotFound()
    {
        var problem = await tms.ResolveRefusedAsync("EX-9999999", Resolution(), HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ExceptionNotFound");
        problem["correlationId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Resolve_AResolvedException_IsRefused_WithExceptionNotOpen_AndPublishesNothingMore()
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
        await tms.ResolvedAsync(exceptionNumber, Resolution(), _cancellationToken);

        var problem = await tms.ResolveRefusedAsync(exceptionNumber, Resolution("NoActionNeeded", "Again"), HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ExceptionNotOpen");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Count(m => m.Subject == "ExceptionResolved" && BodyOf(m)["exceptionNumber"]!.GetValue<string>() == exceptionNumber).Should().Be(1);
    }

    [Fact]
    public async Task AddNote_ToAnOpenException_IsReturnedByTheList_OnANewCorrelationId_AndPublishesNothing()
    {
        var (shipmentNumber, _, openingCorrelationId) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
        await tms.PublishedAsync(openingCorrelationId, "ExceptionOpened", _cancellationToken);

        using var response = await tms.AddNoteAsync(exceptionNumber, Note(), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var added = JsonNode.Parse(body)!;
        var noteCorrelationId = added["correlationId"]!.GetValue<string>();
        noteCorrelationId.Should().NotBe(openingCorrelationId, "each Planner action gets a new correlation ID from the TMS");
        var open = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        var note = open["notes"]!.AsArray().Should().ContainSingle().Subject!;
        note["text"]!.GetValue<string>().Should().Be("Phoned Kessler, redelivery Thursday");
        note["planner"]!.GetValue<string>().Should().Be("Anna de Vries");
        note["addedAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Should().NotContain(m => m.CorrelationId == noteCorrelationId, "a note publishes nothing");
    }

    [Fact]
    public async Task AddNote_WithoutTextOrPlanner_IsRefused_AndAddsNothing()
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();

        using var withoutText = await tms.AddNoteAsync(exceptionNumber, Note(text: " "), _cancellationToken);
        using var withoutPlanner = await tms.AddNoteAsync(exceptionNumber, Note(planner: null), _cancellationToken);

        withoutText.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withoutPlanner.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var tooLong = await tms.AddNoteAsync(exceptionNumber, Note(text: new string('x', 2001)), _cancellationToken);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["notes"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task AddNote_ToAResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var exceptionNumber = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
        await tms.ResolvedAsync(exceptionNumber, Resolution(), _cancellationToken);

        using var response = await tms.AddNoteAsync(exceptionNumber, Note(), _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        JsonNode.Parse(body)!["code"]!.GetValue<string>().Should().Be("ExceptionNotOpen");
    }

    [Fact]
    public async Task AddNote_ToAnUnknownException_IsRefused_WithExceptionNotFound()
    {
        using var response = await tms.AddNoteAsync("EX-9999999", Note(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_NeedsTheManageRole()
    {
        using var withoutToken = tms.CreateTmsClient(token: null);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/exceptions/EX-0000001/resolve", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(Resolution()),
        };
        using var unauthenticated = await withoutToken.SendAsync(request, _cancellationToken);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var tracking = tms.CreateTmsClient(tms.IssueToken([TmsFixture.TrackingRecordRole]));
        using var forbiddenRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/exceptions/EX-0000001/resolve", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(Resolution()),
        };
        using var forbidden = await tracking.SendAsync(forbiddenRequest, _cancellationToken);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OpenExceptions_NeedsATmsToken()
    {
        using var withoutToken = tms.CreateTmsClient(token: null);

        using var response = await withoutToken.GetAsync(new Uri("/v1/exceptions/open", UriKind.Relative), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("CollectionFailed")]
    [InlineData("Damaged")]
    public async Task ACollectionFailedOrDamagedEvent_OpensAnExceptionOfThatType_WithOneExceptionOpened(string type)
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(TrackingEventCalls.Event(type, reference, DateTimeOffset.UtcNow.AddMinutes(-1), "evt-" + type), _cancellationToken, correlationId);

        var body = BodyOf((await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject);
        var results = CanonicalModelSchemas.ValidateExceptionOpened(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["exceptionType"]!.GetValue<string>().Should().Be(type);
        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Which["type"]!.GetValue<string>().Should().Be(type);
    }

    [Fact]
    public async Task DifferentTypes_CanBeOpenAtOnceOnOneShipment_AndASecondCauseOfAnOpenTypeIsAddedToIt()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);

        await tms.ReportDeliveryFailedAsync(reference, _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Damaged", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);
        var secondDamagedCorrelationId = TmsCalls.NewCorrelationId();
        await tms.RecordedAsync(TrackingEventCalls.Event("Damaged", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken, secondDamagedCorrelationId);

        var open = await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken);
        open.Select(o => o["type"]!.GetValue<string>()).Should().BeEquivalentTo("DeliveryFailed", "Damaged");
        open.Single(o => o["type"]!.GetValue<string>() == "Damaged")["numberOfCauses"]!.GetValue<int>().Should().Be(2);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == secondDamagedCorrelationId).Select(m => m.Subject).Should().Equal(["ShipmentExceptionReported"]);
    }

    [Fact]
    public async Task AfterResolving_ANewCauseOfTheSameType_OpensANewException()
    {
        var (shipmentNumber, reference, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        var first = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
        await tms.ResolvedAsync(first, Resolution(), _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.ReportDeliveryFailedAsync(reference, _cancellationToken, correlationId);

        await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken);
        var open = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        open["exceptionNumber"]!.GetValue<string>().Should().NotBe(first);
        open["numberOfCauses"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task AnExceptionEventAfterDelivered_OpensAnExceptionMarkedAfterDelivered_AndTheShipmentStaysDelivered()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(TrackingEventCalls.Event("Damaged", reference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken, correlationId);

        BodyOf((await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject)["afterDelivered"]!.GetValue<bool>().Should().BeTrue();
        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Which["afterDelivered"]!.GetValue<bool>().Should().BeTrue();
        (await tms.GetShipmentAsync(shipmentNumber, _cancellationToken))["status"]!.GetValue<string>().Should().Be("Delivered");
    }

    [Fact]
    public async Task AProofOfDeliveryWithReservations_OpensOneReservationsException_AndAnotherJoinsIt()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        var metadata = ProofOfDeliveryCalls.Metadata(reference, documentId: "doc-reservations-1");
        metadata["condition"] = "Reservations";

        await tms.RecordedProofAsync(metadata, [ProofOfDeliveryCalls.Cmr()], _cancellationToken, correlationId);

        var body = BodyOf((await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject);
        var results = CanonicalModelSchemas.ValidateExceptionOpened(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["exceptionType"]!.GetValue<string>().Should().Be("Reservations");
        body["cause"]!["kind"]!.GetValue<string>().Should().Be("ProofOfDelivery");
        body["cause"]!["sourceEventId"]!.GetValue<string>().Should().Be("doc-reservations-1");
        body["afterDelivered"]!.GetValue<bool>().Should().BeFalse();

        var second = ProofOfDeliveryCalls.Metadata(reference, documentId: "doc-reservations-2");
        second["condition"] = "Reservations";
        var secondCorrelationId = TmsCalls.NewCorrelationId();
        await tms.RecordedProofAsync(second, [ProofOfDeliveryCalls.Cmr()], _cancellationToken, secondCorrelationId);

        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Which["numberOfCauses"]!.GetValue<int>().Should().Be(2);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == secondCorrelationId).Select(m => m.Subject).Should().NotContain("ExceptionOpened");
    }

    [Fact]
    public async Task ACleanProofOfDelivery_OpensNoException()
    {
        var (shipmentNumber, reference) = await tms.DeliveredShipmentAsync(_cancellationToken);

        await tms.RecordedProofAsync(ProofOfDeliveryCalls.Metadata(reference), [ProofOfDeliveryCalls.Cmr()], _cancellationToken);

        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().BeEmpty();
    }
}
