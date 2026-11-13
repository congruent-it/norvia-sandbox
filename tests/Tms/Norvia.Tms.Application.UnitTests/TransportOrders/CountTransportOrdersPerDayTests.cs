using Ardalis.SharedKernel;
using Norvia.Tms.Application.TransportOrders.CountPerDay;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class CountTransportOrdersPerDayTests
{
    private readonly IReadRepository<TransportOrder> _repository = Substitute.For<IReadRepository<TransportOrder>>();

    [Fact]
    public async Task Handle_CountsTheTransportOrdersReceivedEachUtcDay_OldestDayFirst_AndLeavesOutDaysWithNone()
    {
        var day = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        _repository.ListAsync(Arg.Any<TransportOrdersReceivedBetweenSpec>(), Arg.Any<CancellationToken>()).Returns(
        [
            day.AddDays(2).AddHours(23),
            day.AddHours(1),
            day.AddHours(22),
        ]);

        var counts = await new CountTransportOrdersPerDayHandler(_repository)
            .Handle(new CountTransportOrdersPerDayQuery(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12)), CancellationToken.None);

        counts.Should().Equal(
            new TransportOrderDayCount(new DateOnly(2026, 11, 10), 2),
            new TransportOrderDayCount(new DateOnly(2026, 11, 12), 1));
    }

    [Fact]
    public async Task Handle_ReadsTheWholeWindowInUtc_FromTheStartOfItsFirstDayToTheEndOfItsLast()
    {
        _repository.ListAsync(Arg.Any<TransportOrdersReceivedBetweenSpec>(), Arg.Any<CancellationToken>()).Returns(new List<DateTimeOffset>());

        await new CountTransportOrdersPerDayHandler(_repository)
            .Handle(new CountTransportOrdersPerDayQuery(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 12)), CancellationToken.None);

        await _repository.Received().ListAsync(
            Arg.Is<TransportOrdersReceivedBetweenSpec>(spec => spec.From == new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero) && spec.Until == new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero)),
            Arg.Any<CancellationToken>());
    }
}
