using System.Net;
using static Norvia.Tms.TestSupport.ConsigneeTracking.ConsigneeTrackingCalls;

namespace Norvia.Tms.Api.IntegrationTests.ConsigneeTracking;

/// <summary>
/// A code works for 30 days after the Transport Order's delivery, or after its last progress Tracking event if it never is, and then
/// gives the same answer as a code that never existed (ADR 0052).
/// </summary>
public sealed class ConsigneeTrackingExpiryTests(ControlledClockTmsFixture tms) : IClassFixture<ControlledClockTmsFixture>, IDisposable
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public void Dispose() => tms.ResetClock();

    [Fact]
    public async Task View_ADeliveredTransportOrdersCode_WorksUpTo30DaysAfterDelivery_ThenIsTheAnswerForAnUnknownCode()
    {
        var (_, reference, _, code) = await tms.BookedWithCodeAsync(_cancellationToken);
        var delivered = _now.AddHours(-1);
        await tms.RecordedAsync(TrackingEventCalls.Event("Delivered", reference, delivered), _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        using var unknown = await tms.ViewAsync(tms.ConsigneeTracking, UnknownCode, _cancellationToken, correlationId);

        tms.SetClock(delivered.AddDays(30).AddMinutes(-1));
        using var justBefore = await tms.ViewAsync(tms.ConsigneeTracking, code, _cancellationToken, correlationId);
        tms.SetClock(delivered.AddDays(30).AddMinutes(1));
        using var justAfter = await tms.ViewAsync(tms.ConsigneeTracking, code, _cancellationToken, correlationId);

        justBefore.StatusCode.Should().Be(HttpStatusCode.OK);
        justAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await justAfter.Content.ReadAsStringAsync(_cancellationToken)).WithoutTraceId().Should().Be((await unknown.Content.ReadAsStringAsync(_cancellationToken)).WithoutTraceId());
    }

    [Fact]
    public async Task View_ATransportOrderThatNeverMoves_ExpiresAfter30DaysFromWhenItWasReceived()
    {
        var (_, code) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        tms.SetClock(_now.AddDays(29));
        using var within = await tms.ViewAsync(tms.ConsigneeTracking, code, _cancellationToken);

        tms.SetClock(_now.AddDays(31));
        using var beyond = await tms.ViewAsync(tms.ConsigneeTracking, code, _cancellationToken);

        within.StatusCode.Should().Be(HttpStatusCode.OK);
        beyond.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task View_ANewCodeOnAnExpiredTransportOrder_IsAlsoExpired()
    {
        var (number, _) = await tms.AcceptedWithCodeAsync(_cancellationToken);
        tms.SetClock(_now.AddDays(31));

        var replacement = await tms.ReplacedTrackingCodeOfAsync(number, _cancellationToken);
        using var response = await tms.ViewAsync(tms.ConsigneeTracking, replacement, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
