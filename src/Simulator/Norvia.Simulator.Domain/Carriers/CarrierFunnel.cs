namespace Norvia.Simulator.Domain.Carriers;

/// <summary>
/// Makes the journeys of the Shipments assigned to a Carrier: exactly the share asked for at each step, at positions the seed
/// picks, so the same seed and the same number of Shipments give the same journeys. It is worked out once the Planner has
/// assigned, when it is known how many Shipments went to a Carrier.
/// </summary>
public static class CarrierFunnel
{
    // Apart from the bookings' and the Planner's own random sequences.
    private const int CarrierSalt = 0x43415252;

    private static readonly CarrierEvent[] StopPoints = [CarrierEvent.Collected, CarrierEvent.Departed, CarrierEvent.Arrived, CarrierEvent.OutForDelivery];

    public static IReadOnlyList<CarrierJourney> Plan(CarrierShares shares, int seed, int shipmentCount)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentOutOfRangeException.ThrowIfNegative(shipmentCount);

        var random = new SeededRandom(unchecked(seed ^ CarrierSalt));
        var all = Enumerable.Range(0, shipmentCount).ToList();

        var released = Pick(random, all, shares.ReleasePercent);
        var confirmed = Pick(random, released, shares.ConfirmPercent);
        var declined = Pick(random, released.Except(confirmed).ToList(), shares.DeclinePercent, ofAtMost: released.Count);
        var tracked = Pick(random, confirmed, shares.TrackedPercent);

        var exceptions = Exceptions(random, tracked, shares.ExceptionPercent);
        var fails = exceptions.Where(pair => pair.Value is CarrierExceptionKind.CollectionFailed or CarrierExceptionKind.DeliveryFailed).Select(pair => pair.Key).ToHashSet();
        var reservations = exceptions.Where(pair => pair.Value == CarrierExceptionKind.Reservations).Select(pair => pair.Key).ToList();

        var delivered = WithFirst(random, reservations, tracked.Where(index => !fails.Contains(index) && !reservations.Contains(index)).ToList(), shares.DeliveredPercent, tracked.Count - fails.Count);
        var proofs = WithFirst(random, reservations, delivered.Except(reservations).ToList(), shares.ProofOfDeliveryPercent, delivered.Count);
        var cancelled = Pick(random, exceptions.Where(pair => pair.Value == CarrierExceptionKind.CollectionFailed).Select(pair => pair.Key).Order().ToList(), shares.CancelPercent).ToHashSet();

        var releasedSet = released.ToHashSet();
        var confirmedSet = confirmed.ToHashSet();
        var declinedSet = declined.ToHashSet();
        var trackedSet = tracked.ToHashSet();
        var deliveredSet = delivered.ToHashSet();
        var proofSet = proofs.ToHashSet();

        return
        [
            .. all.Select(index => new CarrierJourney(
                index,
                releasedSet.Contains(index),
                !releasedSet.Contains(index) ? CarrierAnswerKind.None
                    : confirmedSet.Contains(index) ? CarrierAnswerKind.Confirm
                    : declinedSet.Contains(index) ? CarrierAnswerKind.Decline
                    : CarrierAnswerKind.NoAnswer,
                trackedSet.Contains(index),
                exceptions.TryGetValue(index, out var kind) ? kind : null,
                deliveredSet.Contains(index),
                proofSet.Contains(index),
                cancelled.Contains(index),
                trackedSet.Contains(index) && !deliveredSet.Contains(index) && !fails.Contains(index) ? random.Pick(StopPoints) : null)),
        ];
    }

    public static CarrierFunnelCounts Count(IReadOnlyList<CarrierJourney> journeys)
    {
        ArgumentNullException.ThrowIfNull(journeys);

        return new CarrierFunnelCounts(
            journeys.Count,
            journeys.Count(journey => journey.Released),
            journeys.Count(journey => journey.Answer == CarrierAnswerKind.Confirm),
            journeys.Count(journey => journey.Answer == CarrierAnswerKind.Decline),
            journeys.Count(journey => journey.Answer == CarrierAnswerKind.NoAnswer),
            journeys.Count(journey => journey.Tracked),
            journeys.Count(journey => journey.Exception is not null),
            journeys.Count(journey => journey.Delivered),
            journeys.Count(journey => journey.ProofOfDelivery),
            journeys.Count(journey => journey.Cancel));
    }

    // Exactly the percentage of the items, picked by the seed, in their own order.
    private static List<int> Pick(SeededRandom random, List<int> items, int percent, int? ofAtMost = null)
    {
        var wanted = (int)Math.Round((ofAtMost ?? items.Count) * percent / 100.0, MidpointRounding.AwayFromZero);
        var chosen = random.Shuffled(items.Count).Take(Math.Min(wanted, items.Count)).ToHashSet();
        return [.. items.Where((_, position) => chosen.Contains(position))];
    }

    // The tracked Shipments that hit an Exception, each with the kind it hits: the kinds in an order the seed picks, dealt in turn so the mix is even.
    private static Dictionary<int, CarrierExceptionKind> Exceptions(SeededRandom random, List<int> tracked, int percent)
    {
        var kinds = random.Shuffled(Enum.GetValues<CarrierExceptionKind>().Length).Select(number => (CarrierExceptionKind)number).ToArray();
        return Pick(random, tracked, percent)
            .Select((index, turn) => (index, kind: kinds[turn % kinds.Length]))
            .ToDictionary(pair => pair.index, pair => pair.kind);
    }

    // The ones that must be in come first and count towards the share asked for of the whole; the others make up the rest.
    private static List<int> WithFirst(SeededRandom random, List<int> musts, List<int> others, int percent, int ofCount)
    {
        var wanted = (int)Math.Round(ofCount * percent / 100.0, MidpointRounding.AwayFromZero);
        return [.. musts, .. Pick(random, others, 100, ofAtMost: Math.Max(0, wanted - musts.Count))];
    }
}
