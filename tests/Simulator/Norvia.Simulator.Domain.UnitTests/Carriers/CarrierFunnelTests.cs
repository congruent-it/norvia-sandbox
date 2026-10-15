using System.Text.Json;
using Norvia.Simulator.Domain.Carriers;

namespace Norvia.Simulator.Domain.UnitTests.Carriers;

public sealed class CarrierFunnelTests
{
    private static readonly CarrierShares Everything = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 0,
        DeliveredPercent: 100, ProofOfDeliveryPercent: 100, CancelPercent: 0);

    private static IReadOnlyList<CarrierJourney> Plan(CarrierShares shares, int count = 100, int seed = 7) => CarrierFunnel.Plan(shares, seed, count);

    [Fact]
    public void Plan_GivesOneJourneyPerShipment_InOrder()
    {
        Plan(Everything, count: 12).Select(journey => journey.Index).Should().Equal(Enumerable.Range(0, 12));
    }

    [Fact]
    public void Plan_NoShipments_GivesNoJourneys()
    {
        Plan(Everything, count: 0).Should().BeEmpty();
    }

    [Fact]
    public void Plan_TheSameSeed_GivesTheSameJourneys()
    {
        var shares = Everything with { ReleasePercent = 60, ConfirmPercent = 50, DeclinePercent = 30, ExceptionPercent = 20, DeliveredPercent = 70, CancelPercent = 50 };

        JsonSerializer.Serialize(Plan(shares, seed: 42)).Should().Be(JsonSerializer.Serialize(Plan(shares, seed: 42)));
    }

    [Fact]
    public void Plan_AnotherSeed_GivesOtherJourneys()
    {
        var shares = Everything with { ReleasePercent = 60, ConfirmPercent = 50, DeclinePercent = 30 };

        JsonSerializer.Serialize(Plan(shares, seed: 1)).Should().NotBe(JsonSerializer.Serialize(Plan(shares, seed: 2)));
    }

    [Fact]
    public void Plan_EverythingAtHundred_DeliversEveryShipmentWithItsProof()
    {
        var journeys = Plan(Everything, count: 20);

        journeys.Should().OnlyContain(journey => journey.Released && journey.Answer == CarrierAnswerKind.Confirm && journey.Tracked && journey.Delivered && journey.ProofOfDelivery);
    }

    [Fact]
    public void Plan_AReleaseShare_ReleasesExactlyThatShare()
    {
        Plan(Everything with { ReleasePercent = 25 }, count: 40).Count(journey => journey.Released).Should().Be(10);
    }

    [Fact]
    public void Plan_ShipmentsThatAreNotReleased_GetNoAnswerAndGoNoFurther()
    {
        var journeys = Plan(Everything with { ReleasePercent = 50 }, count: 20);

        journeys.Where(journey => !journey.Released)
            .Should().HaveCount(10).And.OnlyContain(journey => journey.Answer == CarrierAnswerKind.None && !journey.Tracked && !journey.Delivered && !journey.ProofOfDelivery);
    }

    [Fact]
    public void Plan_ConfirmAndDeclineShares_AreSharesOfTheReleasedAndTheRestGiveNoAnswer()
    {
        var journeys = Plan(Everything with { ReleasePercent = 80, ConfirmPercent = 50, DeclinePercent = 25 }, count: 40);

        journeys.Count(journey => journey.Answer == CarrierAnswerKind.Confirm).Should().Be(16);
        journeys.Count(journey => journey.Answer == CarrierAnswerKind.Decline).Should().Be(8);
        journeys.Count(journey => journey.Released && journey.Answer == CarrierAnswerKind.NoAnswer).Should().Be(8);
    }

    [Fact]
    public void Plan_OnlyConfirmedShipments_AreTracked()
    {
        var journeys = Plan(Everything with { ConfirmPercent = 50, DeclinePercent = 50 }, count: 40);

        journeys.Where(journey => journey.Tracked).Should().OnlyContain(journey => journey.Answer == CarrierAnswerKind.Confirm);
    }

    [Fact]
    public void Plan_ATrackedShare_IsAShareOfTheConfirmed()
    {
        var journeys = Plan(Everything with { ConfirmPercent = 50, DeclinePercent = 0, TrackedPercent = 50 }, count: 40);

        journeys.Count(journey => journey.Tracked).Should().Be(10);
    }

    [Fact]
    public void Plan_ADeliveredShare_IsAShareOfTheTrackedThatDidNotFail()
    {
        var journeys = Plan(Everything with { DeliveredPercent = 50 }, count: 40);

        journeys.Count(journey => journey.Delivered).Should().Be(20);
        journeys.Where(journey => journey.Tracked && !journey.Delivered).Should().OnlyContain(journey => journey.StopsAt != null);
    }

    [Fact]
    public void Plan_AProofShare_IsAShareOfTheDelivered()
    {
        var journeys = Plan(Everything with { DeliveredPercent = 50, ProofOfDeliveryPercent = 50 }, count: 40);

        journeys.Count(journey => journey.ProofOfDelivery).Should().Be(10);
        journeys.Where(journey => journey.ProofOfDelivery).Should().OnlyContain(journey => journey.Delivered);
    }

    [Fact]
    public void Plan_AnExceptionShare_IsExactlyThatShareOfTheTracked()
    {
        var journeys = Plan(Everything with { ExceptionPercent = 25 }, count: 40);

        journeys.Count(journey => journey.Exception is not null).Should().Be(10);
        journeys.Where(journey => journey.Exception is not null).Should().OnlyContain(journey => journey.Tracked);
    }

    [Fact]
    public void Plan_TheExceptionsMix_UsesEveryKind()
    {
        var journeys = Plan(Everything with { ExceptionPercent = 100 }, count: 40);

        journeys.Select(journey => journey.Exception).Distinct().Should().BeEquivalentTo(Enum.GetValues<CarrierExceptionKind>().Cast<CarrierExceptionKind?>());
    }

    [Fact]
    public void Plan_AFailedCollectionOrDelivery_IsNeverDelivered()
    {
        var journeys = Plan(Everything with { ExceptionPercent = 100 }, count: 40);

        journeys.Where(journey => journey.Exception is CarrierExceptionKind.CollectionFailed or CarrierExceptionKind.DeliveryFailed)
            .Should().NotBeEmpty().And.OnlyContain(journey => !journey.Delivered && !journey.ProofOfDelivery);
    }

    [Fact]
    public void Plan_DamageAndReservations_StillDeliver_AndReservationsAlwaysComeWithAProof()
    {
        var journeys = Plan(Everything with { ExceptionPercent = 100 }, count: 40);

        journeys.Where(journey => journey.Exception is CarrierExceptionKind.Damaged).Should().NotBeEmpty().And.OnlyContain(journey => journey.Delivered);
        journeys.Where(journey => journey.Exception is CarrierExceptionKind.Reservations).Should().NotBeEmpty().And.OnlyContain(journey => journey.Delivered && journey.ProofOfDelivery);
    }

    [Fact]
    public void Plan_ACancelShare_IsAShareOfTheFailedCollections()
    {
        var journeys = Plan(Everything with { ExceptionPercent = 100, CancelPercent = 50 }, count: 80);

        var failedCollections = journeys.Where(journey => journey.Exception == CarrierExceptionKind.CollectionFailed).ToList();
        failedCollections.Count(journey => journey.Cancel).Should().Be((int)Math.Round(failedCollections.Count * 0.5, MidpointRounding.AwayFromZero));
        journeys.Where(journey => journey.Exception != CarrierExceptionKind.CollectionFailed).Should().OnlyContain(journey => !journey.Cancel);
    }

    [Fact]
    public void Plan_AShipmentThatStopsEnRoute_StopsAtAProgressEventBeforeDelivery()
    {
        var journeys = Plan(Everything with { DeliveredPercent = 0 }, count: 40);

        journeys.Should().OnlyContain(journey => journey.StopsAt != null && journey.StopsAt != CarrierEvent.Delivered);
    }

    [Fact]
    public void Events_ADeliveredShipment_ReportsEveryProgressEventInOrderAndEndsDelivered()
    {
        var journey = Plan(Everything, count: 1).Single();

        journey.Events.Should().Equal(CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived, CarrierEvent.OutForDelivery, CarrierEvent.Delivered);
    }

    [Fact]
    public void Events_AFailedCollection_ReportsOnlyTheFailure()
    {
        var journey = new CarrierJourney(0, true, CarrierAnswerKind.Confirm, true, CarrierExceptionKind.CollectionFailed, false, false, false, null);

        journey.Events.Should().Equal(CarrierEvent.CollectionFailed);
    }

    [Fact]
    public void Events_AFailedDelivery_GoesOutForDeliveryAndThenFails()
    {
        var journey = new CarrierJourney(0, true, CarrierAnswerKind.Confirm, true, CarrierExceptionKind.DeliveryFailed, false, false, false, null);

        journey.Events.Should().Equal(CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived, CarrierEvent.OutForDelivery, CarrierEvent.DeliveryFailed);
    }

    [Fact]
    public void Events_DamageOnTheWay_IsReportedAfterTheDepartureAndTheShipmentGoesOn()
    {
        var journey = new CarrierJourney(0, true, CarrierAnswerKind.Confirm, true, CarrierExceptionKind.Damaged, true, false, false, null);

        journey.Events.Should().Equal(CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Damaged, CarrierEvent.Arrived, CarrierEvent.OutForDelivery, CarrierEvent.Delivered);
    }

    [Fact]
    public void Events_AShipmentThatStopsEnRoute_EndsAtItsLastEvent()
    {
        var journey = new CarrierJourney(0, true, CarrierAnswerKind.Confirm, true, null, false, false, false, CarrierEvent.Arrived);

        journey.Events.Should().Equal(CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived);
    }

    [Fact]
    public void Events_AShipmentThatIsNotTracked_HasNone()
    {
        new CarrierJourney(0, true, CarrierAnswerKind.Confirm, false, null, false, false, false, null).Events.Should().BeEmpty();
    }

    [Fact]
    public void Counts_SummariseTheFunnel()
    {
        var journeys = Plan(Everything with { ReleasePercent = 50, ConfirmPercent = 50, DeclinePercent = 25 }, count: 40);

        var counts = CarrierFunnel.Count(journeys);

        counts.Shipments.Should().Be(40);
        counts.Released.Should().Be(20);
        counts.Confirmed.Should().Be(10);
        counts.Declined.Should().Be(5);
        counts.NoAnswer.Should().Be(5);
        counts.Tracked.Should().Be(10);
        counts.Delivered.Should().Be(10);
        counts.ProofsOfDelivery.Should().Be(10);
    }
}
