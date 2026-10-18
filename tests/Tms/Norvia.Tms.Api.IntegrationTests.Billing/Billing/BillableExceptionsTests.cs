using System.Text.Json.Nodes;

namespace Norvia.Tms.Api.IntegrationTests.Billing;

/// <summary>
/// An open Exception (P7) blocks a Transport Order from being Billable, and resolving the last one, whatever the outcome,
/// checks billability again (P9). Once Billable, nothing later reopens the order or publishes the event again.
/// </summary>
public sealed class BillableExceptionsTests(TmsFixture tms) : IClassFixture<TmsFixture>
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Redelivered")]
    [InlineData("NoActionNeeded")]
    public async Task ProofOfDeliveryWithReservations_KeepsTheOrderNotYetBillable_UntilItsExceptionIsResolved_WhateverTheOutcome(string outcome)
    {
        var booked = await DeliveredOrderAsync();
        await tms.ProofWithReservationsRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);

        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("NotYetBillable", "the Reservations Exception is open");
        tms.BillablePublished(booked.TransportOrderNumber).Should().BeEmpty();
        var exception = (await tms.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken)).Should().ContainSingle().Subject;

        var resolved = await tms.ResolvedAsync(exception["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution(outcome), _cancellationToken);

        var resolveCorrelationId = resolved["correlationId"]!.GetValue<string>();
        var message = (await tms.PublishedAsync(resolveCorrelationId, "TransportOrderBillable", _cancellationToken)).Should().ContainSingle().Subject;
        JsonNode.Parse(message.Body.ToString())!["correlationId"]!.GetValue<string>().Should().Be(resolveCorrelationId);
        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("Billable");
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.BillablePublished(booked.TransportOrderNumber).Should().ContainSingle();
    }

    [Fact]
    public async Task ExceptionOpenedBeforeTheProofOfDelivery_BlocksTheOrder_EvenWithACleanProof_UntilItIsResolved()
    {
        var booked = await DeliveredOrderAsync();
        await tms.RecordedAsync(TrackingEventCalls.Event("Damaged", booked.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);
        await tms.ProofRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);

        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("NotYetBillable");
        tms.BillablePublished(booked.TransportOrderNumber).Should().BeEmpty();

        var open = await tms.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken);
        var resolved = await tms.ResolvedAsync(open.Single()["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("ClaimHandledOutsideTms"), _cancellationToken);

        await tms.PublishedAsync(resolved["correlationId"]!.GetValue<string>(), "TransportOrderBillable", _cancellationToken);
        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("Billable");
    }

    [Fact]
    public async Task OrderWithTwoOpenExceptions_BecomesBillableOnlyWhenTheLastOneIsResolved()
    {
        var booked = await DeliveredOrderAsync();
        await tms.RecordedAsync(TrackingEventCalls.Event("Damaged", booked.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);
        await tms.ProofWithReservationsRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken);
        var open = await tms.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken);
        open.Should().HaveCount(2);

        await tms.ResolvedAsync(open[0]["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("NoActionNeeded"), _cancellationToken);
        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("NotYetBillable", "one Exception is still open");
        tms.BillablePublished(booked.TransportOrderNumber).Should().BeEmpty();

        var resolved = await tms.ResolvedAsync(open[1]["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("NoActionNeeded"), _cancellationToken);

        await tms.PublishedAsync(resolved["correlationId"]!.GetValue<string>(), "TransportOrderBillable", _cancellationToken);
        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("Billable");
    }

    [Fact]
    public async Task BillableOrder_PublishesNothingMore_ForANewProofOfDelivery_ADeliveryFailedOrDamagedEvent_OrANewException()
    {
        var booked = await DeliveredOrderAsync();
        var billableCorrelationId = TmsCalls.NewCorrelationId();
        await tms.ProofRecordedAsync(booked, billableCorrelationId, _cancellationToken);
        await tms.PublishedAsync(billableCorrelationId, "TransportOrderBillable", _cancellationToken);

        // A Proof of delivery with reservations that replaces the current one, and the Delivery failed and Damaged events after Delivered, each open an Exception.
        await tms.ProofWithReservationsRecordedAsync(booked, TmsCalls.NewCorrelationId(), _cancellationToken, documentId: "replacement");
        await tms.RecordedAsync(TrackingEventCalls.Event("DeliveryFailed", booked.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);
        await tms.RecordedAsync(TrackingEventCalls.Event("Damaged", booked.CarrierBookingReference, DateTimeOffset.UtcNow.AddMinutes(-1)), _cancellationToken);
        var open = await tms.OpenExceptionsOfAsync(booked.ShipmentNumber, _cancellationToken);
        open.Should().HaveCount(3);
        foreach (var exception in open)
        {
            await tms.ResolvedAsync(exception["exceptionNumber"]!.GetValue<string>(), ExceptionCalls.Resolution("NoActionNeeded"), _cancellationToken);
        }

        await TmsFixture.SettleOutboxAsync(_cancellationToken);
        tms.BillablePublished(booked.TransportOrderNumber).Should().ContainSingle("TransportOrderBillable is never published twice");
        (await tms.BillingStateAsync(booked.TransportOrderNumber, _cancellationToken)).Should().Be("Billable");
    }

    private async Task<BillingCalls.BookedOrder> DeliveredOrderAsync()
    {
        var number = await tms.SubmitAcceptedAsync(Bookings.Valid(TmsCalls.NewReference()), TmsCalls.NewCorrelationId(), _cancellationToken);
        await tms.WaitForRatingResultAsync(number, "Rated", _cancellationToken);
        var booked = await tms.BookedAsync(number, _cancellationToken);
        var correlationId = TmsCalls.NewCorrelationId();
        await tms.DeliveredAsync(booked, correlationId, _cancellationToken);
        await tms.PublishedAsync(correlationId, "ShipmentDelivered", _cancellationToken);
        return booked;
    }
}
