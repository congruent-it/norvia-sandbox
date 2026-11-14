using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Carriers;
using Norvia.Erp.Application.MasterData.Carriers.List;
using Norvia.Erp.Application.MasterData.Carriers.Save;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class SaveCarrierHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly IRepository<Carrier> _shippers = Substitute.For<IRepository<Carrier>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private SaveCarrierHandler Handler => new(_shippers, _outbox, _time, NullLogger<SaveCarrierHandler>.Instance);

    private void Have(Carrier shipper) =>
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Carrier>>(), Arg.Any<CancellationToken>()).Returns(shipper);

    [Fact]
    public async Task Handle_ForANewCarrier_AddsItAtRevisionOneAndPublishesTheWholeRecord()
    {
        var result = await Handler.Handle(new SaveCarrierCommand("CAR-0100", "Nieuwe Carrier", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Created);
        result.Value.Carrier.Should().Be(new CarrierView("CAR-0100", "Nieuwe Carrier", true, 1));
        await _shippers.Received().AddAsync(Arg.Is<Carrier>(shipper => shipper.Id.Value == "CAR-0100"), Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e =>
            e is CarrierChangedV1 && e.EventType == "CarrierChanged" && e.Topic == Topics.MasterDataEvents && ((CarrierChangedV1)e).Carrier.Revision == 1));
    }

    [Fact]
    public async Task Handle_ForAChangedCarrier_RaisesTheRevisionAndPublishesIt()
    {
        var shipper = new Carrier(new CarrierId("CAR-0001"), "Old", true);
        Have(shipper);

        var result = await Handler.Handle(new SaveCarrierCommand("CAR-0001", "New", false), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Updated);
        result.Value.Carrier.Should().Be(new CarrierView("CAR-0001", "New", false, 2));
        shipper.Revision.Should().Be(2);
        await _shippers.Received().UpdateAsync(shipper, Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e => ((CarrierChangedV1)e).Carrier.Revision == 2 && !((CarrierChangedV1)e).Carrier.Active));
    }

    [Fact]
    public async Task Handle_ForTheSameContent_ChangesAndPublishesNothing()
    {
        Have(new Carrier(new CarrierId("CAR-0001"), "Same", true));

        var result = await Handler.Handle(new SaveCarrierCommand("CAR-0001", "Same", true), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Unchanged);
        result.Value.Carrier.Revision.Should().Be(1);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _shippers.DidNotReceive().UpdateAsync(Arg.Any<Carrier>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData(" ", "Name")]
    [InlineData("CAR-1", "")]
    public async Task Handle_WithoutAnIdOrAName_IsRefusedWithAStableCode(string id, string name)
    {
        var result = await Handler.Handle(new SaveCarrierCommand(id, name, true), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldRequired");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_ForASaveSentAgainUnderTheSameKey_AnswersAsRepeatAndPublishesNothing()
    {
        var carrier = new Carrier(new CarrierId("CAR-0001"), "Old", true);
        Have(carrier);
        await Handler.Handle(new SaveCarrierCommand("CAR-0001", "New", true, "key-1"), CancellationToken.None);
        _outbox.ClearReceivedCalls();

        var result = await Handler.Handle(new SaveCarrierCommand("CAR-0001", "New", true, "key-1"), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Repeat);
        carrier.Revision.Should().Be(2);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }
}
