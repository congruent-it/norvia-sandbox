using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Shippers;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Application.MasterData.Shippers.Save;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class SaveShipperHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly IRepository<Shipper> _shippers = Substitute.For<IRepository<Shipper>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private SaveShipperHandler Handler => new(_shippers, _outbox, _time, NullLogger<SaveShipperHandler>.Instance);

    private void Have(Shipper shipper) =>
        _shippers.FirstOrDefaultAsync(Arg.Any<ISpecification<Shipper>>(), Arg.Any<CancellationToken>()).Returns(shipper);

    [Fact]
    public async Task Handle_ForANewShipper_AddsItAtRevisionOneAndPublishesTheWholeRecord()
    {
        var result = await Handler.Handle(new SaveShipperCommand("SHP-0100", "Nieuwe Shipper", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Created);
        result.Value.Shipper.Should().Be(new ShipperView("SHP-0100", "Nieuwe Shipper", true, 1));
        await _shippers.Received().AddAsync(Arg.Is<Shipper>(shipper => shipper.Id.Value == "SHP-0100"), Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e =>
            e is ShipperV1 && e.EventType == "Shipper" && e.Topic == Topics.MasterDataEvents && ((ShipperV1)e).Shipper.Revision == 1));
    }

    [Fact]
    public async Task Handle_ForAChangedShipper_RaisesTheRevisionAndPublishesIt()
    {
        var shipper = new Shipper(new ShipperId("SHP-0001"), "Old", true);
        Have(shipper);

        var result = await Handler.Handle(new SaveShipperCommand("SHP-0001", "New", false), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Updated);
        result.Value.Shipper.Should().Be(new ShipperView("SHP-0001", "New", false, 2));
        shipper.Revision.Should().Be(2);
        await _shippers.Received().UpdateAsync(shipper, Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e => ((ShipperV1)e).Shipper.Revision == 2 && !((ShipperV1)e).Shipper.Active));
    }

    [Fact]
    public async Task Handle_ForTheSameContent_ChangesAndPublishesNothing()
    {
        Have(new Shipper(new ShipperId("SHP-0001"), "Same", true));

        var result = await Handler.Handle(new SaveShipperCommand("SHP-0001", "Same", true), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Unchanged);
        result.Value.Shipper.Revision.Should().Be(1);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _shippers.DidNotReceive().UpdateAsync(Arg.Any<Shipper>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData(" ", "Name")]
    [InlineData("SHP-1", "")]
    public async Task Handle_WithoutAnIdOrAName_IsRefusedWithAStableCode(string id, string name)
    {
        var result = await Handler.Handle(new SaveShipperCommand(id, name, true), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldRequired");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_ForASaveSentAgainUnderTheSameKey_AnswersAsRepeatAndPublishesNothing()
    {
        var shipper = new Shipper(new ShipperId("SHP-0001"), "Old", true);
        Have(shipper);
        await Handler.Handle(new SaveShipperCommand("SHP-0001", "Mine", true, "key-1"), CancellationToken.None);
        _outbox.ClearReceivedCalls();
        _shippers.ClearReceivedCalls();

        var result = await Handler.Handle(new SaveShipperCommand("SHP-0001", "Mine", true, "key-1"), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Repeat);
        result.Value.Shipper.Should().Be(new ShipperView("SHP-0001", "Mine", true, 2));
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _shippers.DidNotReceive().UpdateAsync(Arg.Any<Shipper>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForANewKey_EditsAgain()
    {
        var shipper = new Shipper(new ShipperId("SHP-0001"), "Old", true);
        Have(shipper);
        await Handler.Handle(new SaveShipperCommand("SHP-0001", "First", true, "key-1"), CancellationToken.None);

        var result = await Handler.Handle(new SaveShipperCommand("SHP-0001", "Second", true, "key-2"), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Updated);
        result.Value.Shipper.Revision.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ForAKeyOver100Characters_IsRefusedWithAStableCode()
    {
        var result = await Handler.Handle(new SaveShipperCommand("SHP-0001", "Name", true, new string('k', 101)), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataKeyTooLong");
    }
}
