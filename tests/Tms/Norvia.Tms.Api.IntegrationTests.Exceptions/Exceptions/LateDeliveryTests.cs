using System.Text.Json.Nodes;
using static Norvia.Tms.TestSupport.Exceptions.ExceptionCalls;
using static Norvia.Tms.TestSupport.TrackingEvents.TrackingEventCalls;

namespace Norvia.Tms.Api.IntegrationTests.Exceptions;

/// <summary>
/// An ETA after the delivery window opens a Late delivery Exception, and so does the window passing without Delivered, found
/// by the time-based check (P7). Delivered, or an ETA back inside the window, marks it cause cleared.
/// </summary>
public sealed class LateDeliveryTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    // The sample Transport Order's delivery window ends two days from today at 16:00.
    private static DateTimeOffset WindowLatest => DateTimeOffset.UtcNow.Date.AddDays(2).AddHours(16);

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task AnEtaAfterTheWindow_OpensLateDelivery_AndPublishesExceptionOpened_AndFurtherLateEtasPublishNothing()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        var correlationId = TmsCalls.NewCorrelationId();

        await tms.RecordedAsync(Event("Departed", reference, _now.AddMinutes(-1), eta: WindowLatest.AddHours(3)), _cancellationToken, correlationId);
        await tms.RecordedAsync(Event("Arrived", reference, _now.AddMinutes(-1), eta: WindowLatest.AddHours(5)), _cancellationToken);

        var open = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        open["type"]!.GetValue<string>().Should().Be("LateDelivery");
        open["numberOfCauses"]!.GetValue<int>().Should().Be(2);
        var message = (await tms.PublishedAsync(correlationId, "ExceptionOpened", _cancellationToken)).Should().ContainSingle().Subject;
        var results = CanonicalModelSchemas.ValidateExceptionOpened(BodyOf(message));
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        tms.Published.Count(m => m.Subject == "ExceptionOpened" && BodyOf(m)["shipmentNumber"]!.GetValue<string>() == shipmentNumber).Should().Be(1);
    }

    [Fact]
    public async Task TheWindowPassing_OpensLateDeliveryWithoutATrackingEvent_ForTheGraceOfZero()
    {
        var (shipmentNumber, _) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(WindowLatest.AddMinutes(-1));
        await tms.ListShipmentsInProgressAsync(_cancellationToken);
        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().BeEmpty();

        tms.SetClock(WindowLatest);
        await tms.ListShipmentsInProgressAsync(_cancellationToken);
        await tms.ListShipmentsInProgressAsync(_cancellationToken);

        var open = (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().ContainSingle("the check runs again and again but opens one").Subject;
        open["type"]!.GetValue<string>().Should().Be("LateDelivery");
    }

    [Fact]
    public async Task Delivered_MarksLateDeliveryCauseCleared_AndAnEtaBackInsideTheWindowDoesToo()
    {
        var (lateShipment, lateReference) = await tms.BookedShipmentAsync(_cancellationToken);
        var (recoveredShipment, recoveredReference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        await tms.RecordedAsync(Event("Departed", lateReference, _now.AddMinutes(-2), eta: WindowLatest.AddHours(3)), _cancellationToken);
        await tms.RecordedAsync(Event("Departed", recoveredReference, _now.AddMinutes(-2), eta: WindowLatest.AddHours(3)), _cancellationToken);

        await tms.RecordedAsync(Event("Delivered", lateReference, _now.AddMinutes(-1)), _cancellationToken);
        await tms.RecordedAsync(Event("Arrived", recoveredReference, _now.AddMinutes(-1), eta: WindowLatest.AddHours(-1)), _cancellationToken);

        (await tms.OpenExceptionsOfAsync(lateShipment, _cancellationToken)).Should().ContainSingle().Which["causeCleared"]!.GetValue<bool>().Should().BeTrue();
        (await tms.OpenExceptionsOfAsync(recoveredShipment, _cancellationToken)).Should().ContainSingle().Which["causeCleared"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ADeliveredShipment_IsNotCheckedForLateDelivery_WhenTheWindowPasses()
    {
        var (shipmentNumber, reference) = await tms.BookedShipmentAsync(_cancellationToken);
        tms.SetClock(_now);
        await tms.RecordedAsync(Event("Delivered", reference, _now.AddMinutes(-1), eta: WindowLatest.AddHours(3)), _cancellationToken);

        tms.SetClock(WindowLatest.AddHours(1));
        await tms.ListShipmentsInProgressAsync(_cancellationToken);

        (await tms.OpenExceptionsOfAsync(shipmentNumber, _cancellationToken)).Should().BeEmpty();
    }

}
