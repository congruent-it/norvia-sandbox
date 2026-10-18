using System.Net;
using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Exceptions.ExceptionCalls;
using static Norvia.Tms.TestSupport.Exceptions.ShipperNotificationCalls;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>
/// The Planner notifies the Shipper from an open Exception, as often as needed (P7.9): the TMS records a Requested Shipper
/// notification and publishes ShipperNotificationRequested in one transaction. The Shipper notification Logic App reports it Sent
/// or Not delivered; a late or repeat report is ignored. The list of Open Exceptions shows the last notification's status.
/// </summary>
public sealed class NotifyShipperTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Template_PrefillsTheMessageForTheExceptionsType()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();

        using var response = await tms.GetShipperNotificationTemplateAsync(exceptionNumber, _cancellationToken);

        var body = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonNode.Parse(body)!["message"]!.GetValue<string>().Should().Contain("delivery").And.Contain("didn't succeed");
    }

    [Fact]
    public async Task Notify_RecordsARequestedNotification_AndPublishesOneShipperNotificationRequested_OnTheActionsTrail()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();

        using var response = await tms.NotifyShipperAsync(exceptionNumber, _cancellationToken);

        var answer = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, answer);
        var json = JsonNode.Parse(answer)!;
        json["status"]!.GetValue<string>().Should().Be("Requested");
        var correlationId = json["correlationId"]!.GetValue<string>();
        var message = (await tms.PublishedAsync(correlationId, "ShipperNotificationRequested", _cancellationToken)).Should().ContainSingle().Subject;
        var body = BodyOf(message);
        var validation = CanonicalModelSchemas.ValidateShipperNotificationRequested(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["notificationId"]!.GetValue<string>().Should().Be(json["notificationId"]!.GetValue<string>());
        body["exceptionNumber"]!.GetValue<string>().Should().Be(exceptionNumber);
        body["exceptionType"]!.GetValue<string>().Should().Be("DeliveryFailed");
        body["message"]!.GetValue<string>().Should().Be("Your delivery is delayed.");
        body["planner"]!.GetValue<string>().Should().Be("Anna de Vries");
    }

    [Fact]
    public async Task Notify_TwiceForTheSameException_RecordsTwoNotifications()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();

        using var first = await tms.NotifyShipperAsync(exceptionNumber, _cancellationToken);
        using var second = await tms.NotifyShipperAsync(exceptionNumber, _cancellationToken);

        var ids = new[] { first, second }.Select(r => JsonNode.Parse(r.Content.ReadAsStringAsync(_cancellationToken).Result)!["notificationId"]!.GetValue<string>());
        ids.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Sent_MovesTheNotificationToSent_PublishesNothing_AndTheListShowsIt()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        var (notificationId, correlationId) = await NotifiedAsync(exceptionNumber);
        await tms.PublishedAsync(correlationId, "ShipperNotificationRequested", _cancellationToken);
        var before = tms.Published.Count(m => m.CorrelationId == correlationId);

        using var response = await tms.ReportShipperNotificationSentAsync(notificationId, _cancellationToken, correlationId);

        var answer = await response.Content.ReadAsStringAsync(_cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, answer);
        JsonNode.Parse(answer)!["status"]!.GetValue<string>().Should().Be("Sent");
        tms.Published.Count(m => m.CorrelationId == correlationId).Should().Be(before);
        (await LastStatusAsync(exceptionNumber)).Should().Be("Sent");
    }

    [Fact]
    public async Task NotDelivered_MovesTheNotificationToNotDelivered_AndPublishesShipperNotificationNotDelivered_WithTheCause()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        var (notificationId, correlationId) = await NotifiedAsync(exceptionNumber);

        using var response = await tms.ReportShipperNotificationNotDeliveredAsync(notificationId, _cancellationToken, 503, "Service unavailable.", correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = (await tms.PublishedAsync(correlationId, "ShipperNotificationNotDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        var body = BodyOf(message);
        var validation = CanonicalModelSchemas.ValidateShipperNotificationNotDelivered(body);
        validation.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(validation));
        body["notificationId"]!.GetValue<string>().Should().Be(notificationId);
        body["statusCode"]!.GetValue<int>().Should().Be(503);
        body["message"]!.GetValue<string>().Should().Be("Service unavailable.");
        (await LastStatusAsync(exceptionNumber)).Should().Be("NotDelivered");
    }

    [Fact]
    public async Task NotDelivered_WithoutAStatusCode_IsRecorded_AsATimeoutHasNone()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        var (notificationId, correlationId) = await NotifiedAsync(exceptionNumber);

        using var response = await tms.ReportShipperNotificationNotDeliveredAsync(notificationId, _cancellationToken, statusCode: null, message: "Timed out.", correlationId: correlationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = (await tms.PublishedAsync(correlationId, "ShipperNotificationNotDelivered", _cancellationToken)).Should().ContainSingle().Subject;
        var body = BodyOf(message);
        CanonicalModelSchemas.ValidateShipperNotificationNotDelivered(body).IsValid.Should().BeTrue();
        body["statusCode"].Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentReports_OneIsRecorded_TheOtherIgnored_NeitherFails()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        var (notificationId, correlationId) = await NotifiedAsync(exceptionNumber);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => tms.ReportShipperNotificationSentAsync(notificationId, _cancellationToken, correlationId)));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        var ignored = new List<bool>();
        foreach (var response in responses)
        {
            ignored.Add(JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["ignored"]!.GetValue<bool>());
            response.Dispose();
        }

        ignored.Count(i => !i).Should().Be(1);
        (await LastStatusAsync(exceptionNumber)).Should().Be("Sent");
    }

    [Fact]
    public async Task ALateOrRepeatReport_IsIgnored_AndPublishesNothing()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        var (notificationId, correlationId) = await NotifiedAsync(exceptionNumber);
        using var sent = await tms.ReportShipperNotificationSentAsync(notificationId, _cancellationToken, correlationId);
        sent.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = tms.Published.Count(m => m.CorrelationId == correlationId);

        using var repeat = await tms.ReportShipperNotificationSentAsync(notificationId, _cancellationToken, correlationId);
        using var late = await tms.ReportShipperNotificationNotDeliveredAsync(notificationId, _cancellationToken, correlationId: correlationId);

        foreach (var response in new[] { repeat, late })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!["ignored"]!.GetValue<bool>().Should().BeTrue();
        }

        tms.Published.Count(m => m.CorrelationId == correlationId).Should().Be(before);
        (await LastStatusAsync(exceptionNumber)).Should().Be("Sent");
    }

    [Fact]
    public async Task Reports_ForAnUnknownNotification_AreRefused_WithShipperNotificationNotFound()
    {
        using var response = await tms.ReportShipperNotificationSentAsync(Guid.NewGuid().ToString(), _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(_cancellationToken)).Should().Contain("ShipperNotificationNotFound");
    }

    [Fact]
    public async Task Reports_WithoutTheShipperNotificationsReportRole_AreRefused()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        var (notificationId, _) = await NotifiedAsync(exceptionNumber);
        using var noToken = tms.CreateTmsClient(token: null);
        using var carrierLogicApp = tms.CreateTmsClient(tms.IssueToken([TmsFixture.ReportRole]));

        foreach (var (caller, expected) in new[] { (noToken, HttpStatusCode.Unauthorized), (carrierLogicApp, HttpStatusCode.Forbidden) })
        {
            using var sent = await tms.ReportShipperNotificationSentAsync(notificationId, _cancellationToken, caller: caller);
            using var notDelivered = await tms.ReportShipperNotificationNotDeliveredAsync(notificationId, _cancellationToken, caller: caller);
            sent.StatusCode.Should().Be(expected);
            notDelivered.StatusCode.Should().Be(expected);
        }

        (await LastStatusAsync(exceptionNumber)).Should().Be("Requested");
    }

    [Fact]
    public async Task Notify_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();
        await tms.ResolvedAsync(exceptionNumber, Resolution(), _cancellationToken);

        using var response = await tms.NotifyShipperAsync(exceptionNumber, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(_cancellationToken)).Should().Contain("ExceptionNotOpen");
    }

    [Fact]
    public async Task Notify_AnUnknownException_IsRefused_WithExceptionNotFound()
    {
        using var response = await tms.NotifyShipperAsync("EX-9999999", _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null, "Anna")]
    [InlineData("  ", "Anna")]
    [InlineData("Hello", null)]
    public async Task Notify_WithoutAMessageOrPlanner_IsAClientError(string? message, string? planner)
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();

        using var response = await tms.NotifyShipperAsync(exceptionNumber, _cancellationToken, Notification(message, planner));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OpenExceptions_ShowNoNotificationStatus_UntilTheShipperIsNotified()
    {
        var exceptionNumber = await OpenDeliveryFailedExceptionAsync();

        (await LastStatusAsync(exceptionNumber)).Should().BeNull();
    }

    private async Task<string> OpenDeliveryFailedExceptionAsync()
    {
        var (shipmentNumber, _, _) = await tms.DeliveryFailedShipmentAsync(_cancellationToken);
        return (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
    }

    private async Task<(string NotificationId, string CorrelationId)> NotifiedAsync(string exceptionNumber)
    {
        using var response = await tms.NotifyShipperAsync(exceptionNumber, _cancellationToken);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(_cancellationToken))!;
        return (json["notificationId"]!.GetValue<string>(), json["correlationId"]!.GetValue<string>());
    }

    private async Task<string?> LastStatusAsync(string exceptionNumber) =>
        (await tms.ListOpenExceptionsAsync(_cancellationToken))
            .Single(open => open!["exceptionNumber"]!.GetValue<string>() == exceptionNumber)!["lastShipperNotificationStatus"]?.GetValue<string>();
}
