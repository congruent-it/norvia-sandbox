using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Application.MasterData.RateCards.SaveCarrier;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class SaveCarrierRateCardHandlerTests
{
    private static readonly RateCardVersionData Version1 = new(1, new DateOnly(2026, 1, 1), null, [], []);
    private static readonly RateCardVersionData Version1Until = new(1, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), [], []);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly IRepository<CarrierRateCard> _cards = Substitute.For<IRepository<CarrierRateCard>>();
    private readonly IReadRepository<Carrier> _parties = Substitute.For<IReadRepository<Carrier>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    public SaveCarrierRateCardHandlerTests()
    {
        _parties.FirstOrDefaultAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns(new Carrier(new CarrierId("P-1"), "Party", true));
        _cards.ListAsync(Arg.Any<ISpecification<CarrierRateCard>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private SaveCarrierRateCardHandler Handler => new(_cards, _parties, _outbox, _time, NullLogger<SaveCarrierRateCardHandler>.Instance);

    [Fact]
    public async Task Handle_ForANewCard_AddsItAtRevisionOneUnderItsKey()
    {
        var result = await Handler.Handle(new SaveCarrierRateCardCommand("RC-1", "P-1", [Version1], "key-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Created);
        await _cards.Received().AddAsync(Arg.Is<CarrierRateCard>(card => card.Id.Value == "RC-1" && card.IsRepeat("key-1")), Arg.Any<CancellationToken>());
        _outbox.Received(1).Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_ForASaveSentAgainUnderTheSameKey_AnswersAsRepeatAndPublishesNothing()
    {
        var card = CarrierRateCard.CreateByFinance(new RateCardId("RC-1"), new CarrierId("P-1"), [new RateCardVersion(1, new DateOnly(2026, 1, 1), null, [], [])]);
        _cards.FirstOrDefaultAsync(Arg.Any<ISpecification<CarrierRateCard>>(), Arg.Any<CancellationToken>()).Returns(card);
        var first = await Handler.Handle(new SaveCarrierRateCardCommand("RC-1", "P-1", [Version1Until], "key-1"), CancellationToken.None);
        first.Value.Outcome.Should().Be(MasterDataSaveOutcome.Updated);
        _outbox.ClearReceivedCalls();
        _cards.ClearReceivedCalls();

        var result = await Handler.Handle(new SaveCarrierRateCardCommand("RC-1", "P-1", [Version1Until], "key-1"), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Repeat);
        card.Revision.Should().Be(2);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _cards.DidNotReceive().UpdateAsync(Arg.Any<CarrierRateCard>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForAKeyOver100Characters_IsRefusedWithAStableCode()
    {
        var result = await Handler.Handle(new SaveCarrierRateCardCommand("RC-1", "P-1", [Version1], new string('k', 101)), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataKeyTooLong");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }
}
