using Ardalis.Result;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.Reset;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Reset;

public sealed class ResetEstateHandlerTests
{
    private readonly IResetSwitch _setting = Substitute.For<IResetSwitch>();
    private readonly IQuietCheck _quiet = Substitute.For<IQuietCheck>();
    private readonly ITransactionalData _data = Substitute.For<ITransactionalData>();
    private readonly IProofOfDeliveryFiles _files = Substitute.For<IProofOfDeliveryFiles>();

    public ResetEstateHandlerTests()
    {
        _setting.IsOn.Returns(true);
        _quiet.FindBusySubscriptionAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        _data.ClearAsync(Arg.Any<CancellationToken>()).Returns([new DeletedKind("Transport Orders", 4), new DeletedKind("Shipments", 4)]);
        _files.DeleteAllAsync(Arg.Any<CancellationToken>()).Returns(3);
    }

    private Task<Result<ResetReport>> Reset() =>
        new ResetEstateHandler(_setting, _quiet, _data, _files).Handle(new ResetEstateCommand(), CancellationToken.None).AsTask();

    [Fact]
    public async Task Reset_SwitchedOnAndQuiet_ReportsWhatWasDeletedIncludingTheFiles()
    {
        var result = await Reset();

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Equal(new DeletedKind("Transport Orders", 4), new DeletedKind("Shipments", 4), new DeletedKind("Proof of delivery files", 3));
    }

    [Fact]
    public async Task Reset_SwitchedOff_IsForbiddenAndDeletesNothing()
    {
        _setting.IsOn.Returns(false);

        var result = await Reset();

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _data.DidNotReceive().ClearAsync(Arg.Any<CancellationToken>());
        await _files.DidNotReceive().DeleteAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_ASubscriptionHoldsMessages_ConflictsNamingItAndDeletesNothing()
    {
        _quiet.FindBusySubscriptionAsync(Arg.Any<CancellationToken>()).Returns("trip-events/trip-dispatch");

        var result = await Reset();

        result.Status.Should().Be(ResultStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Should().Contain("trip-events/trip-dispatch");
        await _data.DidNotReceive().ClearAsync(Arg.Any<CancellationToken>());
        await _files.DidNotReceive().DeleteAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_TheRecordsCannotBeDeleted_LeavesTheFiles()
    {
        _data.ClearAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<DeletedKind>>>(_ => throw new InvalidOperationException());

        var act = Reset;

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _files.DidNotReceive().DeleteAllAsync(Arg.Any<CancellationToken>());
    }
}

public sealed class GetResetReadinessHandlerTests
{
    private readonly IResetSwitch _setting = Substitute.For<IResetSwitch>();
    private readonly IQuietCheck _quiet = Substitute.For<IQuietCheck>();

    private Task<ResetReadiness> Ask() =>
        new GetResetReadinessHandler(_setting, _quiet).Handle(new GetResetReadinessQuery(), CancellationToken.None).AsTask();

    [Fact]
    public async Task Ask_SwitchedOn_NamesTheBusySubscription()
    {
        _setting.IsOn.Returns(true);
        _quiet.FindBusySubscriptionAsync(Arg.Any<CancellationToken>()).Returns("shipment-events/carrier-booking");

        (await Ask()).Should().Be(new ResetReadiness(true, "shipment-events/carrier-booking"));
    }

    [Fact]
    public async Task Ask_SwitchedOff_DoesNotLookAtTheSubscriptions()
    {
        _setting.IsOn.Returns(false);

        (await Ask()).Should().Be(new ResetReadiness(false, null));
        await _quiet.DidNotReceive().FindBusySubscriptionAsync(Arg.Any<CancellationToken>());
    }
}
