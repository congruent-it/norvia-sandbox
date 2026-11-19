using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.TestSupport.ConsigneeTracking;
using static Norvia.Tms.TestSupport.Exceptions.ExceptionCalls;
using static Norvia.Tms.TestSupport.Exceptions.ShipperNotificationCalls;

namespace Norvia.Integration.ConsigneeNotification.IntegrationTests;

/// <summary>
/// The Consignee is told where to track the goods (#983, ADR 0054), from the Carrier's Collected report to the email the Consignee notification Logic App sends: the TMS adds
/// <c>ConsigneeNotificationRequested</c> to its outbox, the worker that plays the Logic App reads its subscription, asks the TMS for the address and the tracking code, and writes the email to a
/// folder in place of Azure Communication Services. Nothing here is a mock: the TMS, its database, the Service Bus emulator and the worker run as they do on a developer's machine.
/// </summary>
public sealed class ConsigneeNotificationTests(ConsigneeNotificationFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Collected_WithAConsigneeEmail_SendsOneEmail_ThatCarriesTheTrackingLink_AndNothingElse()
    {
        var address = NewAddress();
        var (shipmentNumber, transportOrderNumber, reference) = await fixture.BookedShipmentFor(address, _cancellationToken);
        var code = await fixture.TrackingCodeOfAsync(transportOrderNumber, _cancellationToken);

        var correlationId = await fixture.CollectedAsync(reference, _cancellationToken);

        var requested = (await fixture.PublishedAsync(correlationId, "ConsigneeNotificationRequested", _cancellationToken)).Should().ContainSingle().Subject;
        requested.Body.ToString().Should().NotContainEquivalentOf(address, "the event names the Transport Order and nothing about the Consignee").And.NotContain(code);
        requested.ApplicationProperties.Values.Select(value => value?.ToString()).Should().NotContain(value => value != null && value.Contains(address, StringComparison.OrdinalIgnoreCase));
        var email = await fixture.EmailToAsync(address, _cancellationToken);
        await fixture.WorkerLogAsync(correlationId, "The Consignee notification email was sent", _cancellationToken);
        await ConsigneeNotificationFixture.SettleAsync(_cancellationToken);
        fixture.EmailsTo(address).Should().ContainSingle("one Collected is one email, however often the outbox ran");
        email.Should().Contain($"{ConsigneeNotificationFixture.TrackingBaseAddress}track/{code}");
        email.Should().NotContain(transportOrderNumber).And.NotContain(shipmentNumber, "the email carries the link and nothing else (ADR 0052)");
    }

    [Fact]
    public async Task Collected_WithNoConsigneeEmail_SendsNothing_AndRaisesNoFailure()
    {
        var (_, _, reference) = await fixture.BookedShipmentFor(consigneeEmail: null, _cancellationToken);
        var emailsBefore = fixture.Emails().Count;
        var deadLettersBefore = await fixture.DeadLetteredAsync(_cancellationToken);

        var correlationId = await fixture.CollectedAsync(reference, _cancellationToken);

        // The Collected is published and the outbox has run a few cycles, so a request that should not be made would have been.
        (await fixture.PublishedAsync(correlationId, "ShipmentCollected", _cancellationToken)).Should().ContainSingle();
        fixture.Published.Should().NotContain(message => message.CorrelationId == correlationId && message.Subject == "ConsigneeNotificationRequested");
        await ConsigneeNotificationFixture.SettleAsync(_cancellationToken);
        fixture.Emails().Should().HaveCount(emailsBefore);
        (await fixture.DeadLetteredAsync(_cancellationToken)).Should().Be(deadLettersBefore);
    }

    [Fact]
    public async Task Request_ForATransportOrderWithNothingToSend_IsDone_WithNoEmailAndNoFailure()
    {
        // The email is optional, and an order can lose it between the request and the Logic App's read: the TMS answers 404 and the run is done, not failed.
        var (_, transportOrderNumber, _) = await fixture.BookedShipmentFor(consigneeEmail: null, _cancellationToken);
        var emailsBefore = fixture.Emails().Count;
        var deadLettersBefore = await fixture.DeadLetteredAsync(_cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();

        await fixture.SendRequestAsync(transportOrderNumber, correlationId, _cancellationToken);

        await fixture.WorkerLogAsync(correlationId, "The Transport Order has nothing to send to a Consignee", _cancellationToken);
        await ConsigneeNotificationFixture.SettleAsync(_cancellationToken);
        fixture.Emails().Should().HaveCount(emailsBefore);
        (await fixture.DeadLetteredAsync(_cancellationToken)).Should().Be(deadLettersBefore);
        fixture.WorkerLogsOf(correlationId).Should().OnlyContain(log => log["outcome"] != "Failed" && !log.Text.Contains("run failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collected_WhenTheEmailCannotBeSent_StillCollects_AndTheShipperIsStillNotified()
    {
        var address = NewAddress();
        var (shipmentNumber, _, reference) = await fixture.BookedShipmentFor(address, _cancellationToken);
        var emailsBefore = fixture.Emails().Count;
        string collectedCorrelationId;

        using (fixture.BreakMailbox())
        {
            // The TMS accepts the Collected: a Consignee that can't be told never undoes it.
            collectedCorrelationId = await fixture.CollectedAsync(reference, _cancellationToken);
            await fixture.PublishedAsync(collectedCorrelationId, "ConsigneeNotificationRequested", _cancellationToken);

            // The Shipper's own path, which shares nothing with the Consignee's: the Planner notifies the Shipper while the email is still failing.
            await fixture.ReportDeliveryFailedAsync(reference, _cancellationToken);
            var exceptionNumber = (await fixture.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Single()["exceptionNumber"]!.GetValue<string>();
            using var notified = await fixture.NotifyShipperAsync(exceptionNumber, _cancellationToken);
            var notification = JsonNode.Parse(await notified.Content.ReadAsStringAsync(_cancellationToken))!;
            notified.StatusCode.Should().Be(HttpStatusCode.OK, notification.ToJsonString());
            await fixture.PublishedAsync(notification["correlationId"]!.GetValue<string>(), "ShipperNotificationRequested", _cancellationToken);
            using var sent = await fixture.ReportShipperNotificationSentAsync(notification["notificationId"]!.GetValue<string>(), _cancellationToken);
            sent.StatusCode.Should().Be(HttpStatusCode.OK);

            // The run fails, with the code the operator finds it by, after the retries.
            var failed = await fixture.WorkerLogAsync(collectedCorrelationId, "The Consignee notification email was not sent", _cancellationToken);
            failed["outcome"].Should().Be("Failed");
        }

        fixture.Emails().Should().HaveCount(emailsBefore);
        fixture.Published.Should().NotContain(message => message.CorrelationId == collectedCorrelationId && message.Subject == "ShipperNotificationNotDelivered");
    }

    [Fact]
    public async Task TheEmailAddress_IsInNoLog_WhetherTheEmailWasSentOrNot()
    {
        var sentTo = NewAddress();
        var (_, _, sentReference) = await fixture.BookedShipmentFor(sentTo, _cancellationToken);
        var sentCorrelationId = await fixture.CollectedAsync(sentReference, _cancellationToken);
        await fixture.EmailToAsync(sentTo, _cancellationToken);
        await fixture.WorkerLogAsync(sentCorrelationId, "The Consignee notification email was sent", _cancellationToken);

        var notSentTo = NewAddress();
        var (_, _, notSentReference) = await fixture.BookedShipmentFor(notSentTo, _cancellationToken);
        string notSentCorrelationId;
        using (fixture.BreakMailbox())
        {
            notSentCorrelationId = await fixture.CollectedAsync(notSentReference, _cancellationToken);
            await fixture.WorkerLogAsync(notSentCorrelationId, "The Consignee notification email was not sent", _cancellationToken);
        }

        // Both runs are over and the TMS has logged what it did for them, so the logs hold what they would hold.
        await ConsigneeNotificationFixture.SettleAsync(_cancellationToken);
        foreach (var address in new[] { sentTo, notSentTo })
        {
            fixture.AllLogs.Should().NotContain(log => log.Text.Contains(address, StringComparison.OrdinalIgnoreCase) || log.Text.Contains(address.Split('@')[0], StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string NewAddress() => $"consignee-{Guid.NewGuid():N}@example.test";
}
