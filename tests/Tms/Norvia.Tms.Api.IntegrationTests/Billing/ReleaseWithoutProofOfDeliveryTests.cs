using System.Net;
using System.Text.Json.Nodes;
using Norvia.Tms.Api.IntegrationTests.Exceptions;
using Norvia.Tms.Api.IntegrationTests.TrackingEvents;
using Norvia.Tms.Api.IntegrationTests.TransportOrders;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>
/// Customer service releases a Delivered, Not yet billable Transport Order without a Proof of delivery (P9), so a Carrier that
/// never sends one can't hold up billing for good. The release lifts only <c>NoProofOfDelivery</c>.
/// </summary>
public sealed class ReleaseWithoutProofOfDeliveryTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReleaseOfARatedDeliveredOrder_MakesItBillableWithTheFlag_PublishesOnlyTransportOrderBillable_AndRecordsTheRelease()
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        (await tms.BlockingReasonsAsync(order, _cancellationToken)).Should().Equal("NoProofOfDelivery");

        var released = await tms.ReleasedWithoutProofAsync(order, _cancellationToken);

        released["billingState"]!.GetValue<string>().Should().Be("Billable");
        var correlationId = released["correlationId"]!.GetValue<string>();
        var message = (await tms.PublishedAsync(correlationId, "TransportOrderBillable", _cancellationToken)).Should().ContainSingle().Subject;
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.Published.Where(m => m.CorrelationId == correlationId).Should().ContainSingle("the release publishes nothing of its own");
        tms.BillablePublished(order).Should().ContainSingle();
        var body = JsonNode.Parse(message.Body.ToString())!;
        var results = CanonicalModelSchemas.ValidateTransportOrderBillable(body);
        results.IsValid.Should().BeTrue(CanonicalModelSchemas.Errors(results));
        body["releasedWithoutProofOfDelivery"]!.GetValue<bool>().Should().BeTrue();

        var stored = await tms.GetTransportOrderAsync(order, _cancellationToken);
        stored["billingState"]!.GetValue<string>().Should().Be("Billable");
        var release = stored["proofOfDeliveryRelease"]!;
        release["releasedBy"]!.GetValue<string>().Should().Be("Jan Jansen");
        release["remark"]!.GetValue<string>().Should().Be("CMR received by post, scanned in the dossier");
        DateTimeOffset.Parse(release["releasedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        (await tms.NotYetBillableEntryAsync(order, _cancellationToken)).Should().BeNull("a Billable order leaves the list");
    }

    [Fact]
    public async Task ReleaseOfAnUnratedOrder_LiftsOnlyNoProofOfDelivery_AndRatingItLaterMakesItBillable()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);
        var booked = await tms.BookedAsync(number, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);

        var released = await tms.ReleasedWithoutProofAsync(number, _cancellationToken);

        released["billingState"]!.GetValue<string>().Should().Be("NotYetBillable");
        (await tms.BlockingReasonsAsync(number, _cancellationToken)).Should().Equal("NotRated");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.BillablePublished(number).Should().BeEmpty();
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["proofOfDeliveryRelease"]!["releasedBy"]!.GetValue<string>().Should().Be("Jan Jansen");
    }

    [Fact]
    public async Task ReleaseOfAnOrderWithAnOpenException_LiftsOnlyNoProofOfDelivery_AndResolvingItMakesItBillableWithTheFlag()
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.ProofWithReservationsRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        (await tms.BlockingReasonsAsync(order, _cancellationToken)).Should().Equal("OpenException");

        var released = await tms.ReleasedWithoutProofAsync(order, _cancellationToken);

        released["billingState"]!.GetValue<string>().Should().Be("NotYetBillable");
        (await tms.BlockingReasonsAsync(order, _cancellationToken)).Should().Equal("OpenException");
        tms.BillablePublished(order).Should().BeEmpty();

        var exception = (await tms.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;
        var resolved = await tms.ResolvedAsync(exception["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("NoActionNeeded"), _cancellationToken);
        var message = (await tms.PublishedAsync(resolved["correlationId"]!.GetValue<string>(), "TransportOrderBillable", _cancellationToken)).Should().ContainSingle().Subject;
        JsonNode.Parse(message.Body.ToString())!["releasedWithoutProofOfDelivery"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ProofOfDeliveryThatArrivesAfterTheRelease_IsRecordedAsUsual_AndChangesNothingForBilling()
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var released = await tms.ReleasedWithoutProofAsync(order, _cancellationToken);
        await tms.PublishedAsync(released["correlationId"]!.GetValue<string>(), "TransportOrderBillable", _cancellationToken);

        var proofCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, proofCorrelationId, _cancellationToken);
        await tms.PublishedAsync(proofCorrelationId, "ProofOfDeliveryReceived", _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);

        (await tms.BillingStateAsync(order, _cancellationToken)).Should().Be("Billable");
        tms.BillablePublished(order).Should().ContainSingle("Billable is final");
        (await tms.GetTransportOrderAsync(order, _cancellationToken))["proofOfDeliveryRelease"]!["releasedBy"]!.GetValue<string>().Should().Be("Jan Jansen");
    }

    [Theory]
    [InlineData(null, "Jan Jansen", "Remark")]
    [InlineData("  ", "Jan Jansen", "Remark")]
    [InlineData("CMR by post", null, "ReleasedBy")]
    public async Task ReleaseWithoutARemarkOrWhoReleased_IsRefusedAsABadRequest(string? remark, string? releasedBy, string field)
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);

        var problem = await tms.ReleaseRefusedAsync(order, BillingCalls.Release(remark, releasedBy), HttpStatusCode.BadRequest, _cancellationToken);

        problem["errors"]!.AsObject().Should().ContainKey(field);
        (await tms.BlockingReasonsAsync(order, _cancellationToken)).Should().Equal("NoProofOfDelivery");
    }

    [Fact]
    public async Task ReleaseBeforeTheShipmentIsDelivered_IsRefusedAndChangesNothing()
    {
        var order = await RatedOrderAsync();
        await tms.BookedAsync(order, _cancellationToken);

        var problem = await tms.ReleaseRefusedAsync(order, BillingCalls.Release(), HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("ShipmentNotDelivered");
        problem["correlationId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        (await tms.GetTransportOrderAsync(order, _cancellationToken))["proofOfDeliveryRelease"].Should().BeNull();
    }

    [Fact]
    public async Task ReleaseOfAnOrderThatIsBillableAlready_IsRefusedAndPublishesNothingMore()
    {
        var order = await RatedOrderAsync();
        var booked = await tms.BookedAsync(order, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.ProofRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForBillableAsync(order, _cancellationToken);

        var problem = await tms.ReleaseRefusedAsync(order, BillingCalls.Release(), HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("AlreadyBillable");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.BillablePublished(order).Should().ContainSingle();
        (await tms.GetTransportOrderAsync(order, _cancellationToken))["proofOfDeliveryRelease"].Should().BeNull();
    }

    [Fact]
    public async Task SecondReleaseOfAnOrderThatIsStillNotYetBillable_IsRefusedAndKeepsTheFirst()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()).DeliveredToDusseldorf(), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "RatingFailed", _cancellationToken);
        var booked = await tms.BookedAsync(number, _cancellationToken);
        await tms.DeliveredAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.ReleasedWithoutProofAsync(number, _cancellationToken);

        var problem = await tms.ReleaseRefusedAsync(number, BillingCalls.Release("again", "Piet"), HttpStatusCode.Conflict, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("AlreadyReleased");
        (await tms.GetTransportOrderAsync(number, _cancellationToken))["proofOfDeliveryRelease"]!["releasedBy"]!.GetValue<string>().Should().Be("Jan Jansen");
    }

    [Fact]
    public async Task ReleaseOfAnUnknownOrder_IsNotFound()
    {
        var problem = await tms.ReleaseRefusedAsync("TO-9999999999", BillingCalls.Release(), HttpStatusCode.NotFound, _cancellationToken);

        problem["code"]!.GetValue<string>().Should().Be("TransportOrderNotFound");
    }

    private async Task<string> RatedOrderAsync()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        return number;
    }
}
