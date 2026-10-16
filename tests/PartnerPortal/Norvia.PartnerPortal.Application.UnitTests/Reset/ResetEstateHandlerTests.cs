using Ardalis.Result;
using Norvia.PartnerPortal.Application.Reset;

namespace Norvia.PartnerPortal.Application.UnitTests.Reset;

public sealed class ResetEstateHandlerTests
{
    private readonly IResetSwitch _setting = Substitute.For<IResetSwitch>();
    private readonly ITransactionalData _data = Substitute.For<ITransactionalData>();

    public ResetEstateHandlerTests() =>
        _data.ClearAsync(Arg.Any<CancellationToken>()).Returns([new DeletedKind("Bookings", 4), new DeletedKind("Carrier booking requests", 2)]);

    private Task<Result<ResetReport>> Reset() =>
        new ResetEstateHandler(_setting, _data).Handle(new ResetEstateCommand(), CancellationToken.None).AsTask();

    [Fact]
    public async Task Reset_SwitchedOn_ReportsWhatWasDeleted()
    {
        _setting.IsOn.Returns(true);

        var result = await Reset();

        result.IsSuccess.Should().BeTrue();
        result.Value.Deleted.Should().Equal(new DeletedKind("Bookings", 4), new DeletedKind("Carrier booking requests", 2));
    }

    [Fact]
    public async Task Reset_SwitchedOff_IsForbiddenAndDeletesNothing()
    {
        _setting.IsOn.Returns(false);

        var result = await Reset();

        result.Status.Should().Be(ResultStatus.Forbidden);
        await _data.DidNotReceive().ClearAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ask_ReportsTheSettingAndNeverASubscription(bool on)
    {
        _setting.IsOn.Returns(on);

        var readiness = await new GetResetReadinessHandler(_setting).Handle(new GetResetReadinessQuery(), CancellationToken.None);

        readiness.Should().Be(new ResetReadiness(on, null));
    }
}
