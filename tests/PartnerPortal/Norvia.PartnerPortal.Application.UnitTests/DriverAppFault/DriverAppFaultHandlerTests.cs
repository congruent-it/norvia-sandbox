using Norvia.PartnerPortal.Application.DriverAppFault;
using Norvia.PartnerPortal.Application.DriverAppFault.GetCurrent;
using Norvia.PartnerPortal.Application.DriverAppFault.Switch;

namespace Norvia.PartnerPortal.Application.UnitTests.DriverAppFault;

public sealed class DriverAppFaultHandlerTests
{
    private readonly IDriverAppSettings _driverApp = Substitute.For<IDriverAppSettings>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_ReportsWhatTheDriverAppHas(bool unavailable)
    {
        _driverApp.IsUnavailableAsync(Arg.Any<CancellationToken>()).Returns(unavailable);

        var view = await new GetDriverAppFaultHandler(_driverApp).Handle(new GetDriverAppFaultQuery(), CancellationToken.None);

        view.Should().Be(new DriverAppFaultView(unavailable));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Set_SwitchesTheDriverApp_AndReportsTheNewState(bool unavailable)
    {
        var view = await new SetDriverAppFaultHandler(_driverApp).Handle(new SetDriverAppFaultCommand(unavailable), CancellationToken.None);

        await _driverApp.Received(1).SetUnavailableAsync(unavailable, Arg.Any<CancellationToken>());
        view.Should().Be(new DriverAppFaultView(unavailable));
    }
}
