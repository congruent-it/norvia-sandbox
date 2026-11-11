using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.TestSupport.Exceptions;
using Norvia.Tms.TestSupport.TrackingEvents;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// The Planner agent's draft of an Exception note (P7), for a Shipment each test books itself. <c>get_exception_timeline</c> returns the facts of the Exception and never a Consignee's
/// personal data. A note saved from a draft names the Agent and its run on its trace (ADR 0040), edited or not; one typed by hand names none.
/// </summary>
public sealed class ExceptionNoteDraftTests(PlannerAgentFixture fixture)
{
    // The sample order's delivery window ends two days from today at 16:00.
    private static DateTimeOffset WindowLatest => DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16);

    private static DateTimeOffset LateEta => WindowLatest.AddHours(3);

    // Written out, not read from the handler's constant, so a changed name shows.
    private const string NoteAgentName = "planner-exception-note";

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheTimelineOfALateDelivery_HasTheFactsOfTheLateness_AndNoConsigneePersonalData()
    {
        var (shipmentNumber, reference) = await fixture.BookedShipmentAsync(_cancellationToken);
        await fixture.RecordedAsync(TrackingEventCalls.Event("Departed", reference, DateTimeOffset.UtcNow.AddMinutes(-1), eta: LateEta), _cancellationToken);
        var exceptionNumber = await OpenExceptionOfAsync(shipmentNumber);

        var timeline = await TimelineAsync(exceptionNumber);

        timeline["exceptionNumber"]!.GetValue<string>().Should().Be(exceptionNumber);
        timeline["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        timeline["type"]!.GetValue<string>().Should().Be("LateDelivery");
        timeline["status"]!.GetValue<string>().Should().Be("Open");
        timeline["causes"]!.AsArray().Single()!["kind"]!.GetValue<string>().Should().Be("TrackingEvent");
        var departed = timeline["trackingEvents"]!.AsArray().Single()!;
        departed["type"]!.GetValue<string>().Should().Be("Departed");
        departed["source"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId, "the Planner sees who reported it");
        DateTimeOffset.Parse(departed["eta"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(LateEta);
        timeline["carrierBookings"]!.AsArray().Single()!["status"]!.GetValue<string>().Should().Be("Confirmed");
    }

    [Fact]
    public async Task TheTimelineOfAFailedDelivery_HasTheFailureAndWhatWasDoneSoFar_AndNoConsigneePersonalData()
    {
        var (shipmentNumber, carrierBookingNumber, _, exceptionNumber) = await fixture.BookedWithDeliveryFailedAsync(_cancellationToken);
        using var noted = await fixture.AddNoteAsync(exceptionNumber, ExceptionCalls.Note("Phoned the Carrier, redelivery Thursday"), _cancellationToken);
        noted.StatusCode.Should().Be(HttpStatusCode.OK);

        var timeline = await TimelineAsync(exceptionNumber);

        timeline["type"]!.GetValue<string>().Should().Be("DeliveryFailed");
        timeline["shipmentNumber"]!.GetValue<string>().Should().Be(shipmentNumber);
        timeline["status"]!.GetValue<string>().Should().Be("Open");
        var failed = timeline["trackingEvents"]!.AsArray().Single(e => e!["type"]!.GetValue<string>() == "DeliveryFailed")!;
        failed["source"]!.GetValue<string>().Should().Be(PlanningCalls.CarrierId);
        var booking = timeline["carrierBookings"]!.AsArray().Single()!;
        booking["carrierBookingNumber"]!.GetValue<string>().Should().Be(carrierBookingNumber);
        booking["status"]!.GetValue<string>().Should().Be("Confirmed");
        timeline["carrierBookingCancellations"]!.AsArray().Should().BeEmpty("nothing was cancelled yet");
        timeline["notes"]!.AsArray().Single()!["text"]!.GetValue<string>().Should().Be("Phoned the Carrier, redelivery Thursday");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANoteSavedFromADraft_NamesTheAgentAndItsRunOnItsTrace_EditedOrNot(bool edited)
    {
        var (_, exceptionNumber) = await fixture.CollectedWithDeliveryFailedAsync(_cancellationToken);
        var run = await PlannerAgentCalls.DraftNoteAsync(fixture, PlannerAgentFixture.NewAgentId(), exceptionNumber, Guid.NewGuid().ToString(), _cancellationToken);
        run.Suggestion.Cause.Should().NotContainAny(PlannerAgentCalls.NeverForTheAgent);
        var note = ExceptionCalls.Note(edited ? run.Suggestion.Cause + " Called the depot." : run.Suggestion.Cause);
        note["agentName"] = NoteAgentName;
        note["agentRunId"] = run.RunId;

        var added = await AddedAsync(exceptionNumber, note);

        var trace = await SucceededTraceAsync(added["correlationId"]!.GetValue<string>());
        trace["agentId"].Should().Be(NoteAgentName);
        trace["agentRunId"].Should().Be(run.RunId);
    }

    [Fact]
    public async Task ANoteTypedByHand_NamesNoAgentOnItsTrace()
    {
        var (_, exceptionNumber) = await fixture.CollectedWithDeliveryFailedAsync(_cancellationToken);

        var added = await AddedAsync(exceptionNumber, ExceptionCalls.Note());

        var trace = await SucceededTraceAsync(added["correlationId"]!.GetValue<string>());
        trace["agentId"].Should().BeNull();
        trace["agentRunId"].Should().BeNull();
    }

    private async Task<string> OpenExceptionOfAsync(string shipmentNumber) =>
        (await fixture.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();

    private async Task<JsonNode> AddedAsync(string exceptionNumber, JsonObject note)
    {
        using var response = await fixture.AddNoteAsync(exceptionNumber, note, _cancellationToken);
        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    private Task<TmsLog> SucceededTraceAsync(string correlationId) =>
        fixture.TmsLogAsync(log => log["correlationId"] == correlationId && log["outcome"] == "Succeeded", _cancellationToken);

    // The timeline as the Agent reads it, which holds no Consignee, street or postcode.
    private async Task<JsonNode> TimelineAsync(string exceptionNumber)
    {
        using var client = fixture.CreateMcpClient(fixture.AgentToken());
        var text = await client.TimelineTextOfAsync(exceptionNumber, _cancellationToken);
        text.Should().NotContainAny(PlannerAgentCalls.NeverInATimeline, "the Agent reads no Consignee, street or postcode");
        return JsonNode.Parse(text)!;
    }
}
