using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Infrastructure.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Faults;

public sealed class DriverReportsLogicAppFaultTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
    private readonly ILogicAppSwitch _logicApp = Substitute.For<ILogicAppSwitch>();
    private readonly IFaults _faults;
    private readonly DriverReportsLogicAppFault _fault;
    private readonly string _flag = Path.Combine(Path.GetTempPath(), $"norvia-flag-{Guid.NewGuid():N}");

    public DriverReportsLogicAppFaultTests()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddFaults(PartnerPortalFaultDefinitions.All);
        builder.Services.AddSingleton<TimeProvider>(_time);
        _faults = builder.Services.BuildServiceProvider().GetRequiredService<IFaults>();
        _fault = new DriverReportsLogicAppFault(_faults, _logicApp, new FakeLogger<DriverReportsLogicAppFault>());
    }

    public void Dispose() => File.Delete(_flag);

    [Fact]
    public async Task Reconcile_WhileTheFaultIsOff_AndTheLogicAppIsEnabled_ChangesNothing()
    {
        _logicApp.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _fault.ReconcileAsync(TestContext.Current.CancellationToken);

        await _logicApp.DidNotReceive().SetEnabledAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_WhileTheFaultIsOn_DisablesTheLogicApp()
    {
        _faults.SwitchOn(PartnerPortalFaultNames.DriverReportsLogicAppDisabled, TimeSpan.FromMinutes(2), out _);
        _logicApp.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _fault.ReconcileAsync(TestContext.Current.CancellationToken);

        await _logicApp.Received(1).SetEnabledAsync(false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_AfterTheFaultExpired_EnablesTheLogicApp_ThoughNobodySwitchedItOff()
    {
        _faults.SwitchOn(PartnerPortalFaultNames.DriverReportsLogicAppDisabled, TimeSpan.FromMinutes(2), out _);
        _logicApp.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);
        _time.Advance(TimeSpan.FromMinutes(2));

        await _fault.ReconcileAsync(TestContext.Current.CancellationToken);

        await _logicApp.Received(1).SetEnabledAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_AfterARestart_EnablesALogicAppTheFaultLeftOff()
    {
        _logicApp.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(false);

        await _fault.ReconcileAsync(TestContext.Current.CancellationToken);

        await _logicApp.Received(1).SetEnabledAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheFlagFileSwitch_IsEnabledUntilTheFlagExists_AndSwitchingBackRemovesIt()
    {
        var flag = new FlagFileLogicAppSwitch(Options.Create(new FaultsOptions { LogicApp = new FaultLogicAppOptions { FlagPath = _flag } }));
        var token = TestContext.Current.CancellationToken;

        (await flag.IsEnabledAsync(token)).Should().BeTrue();
        await flag.SetEnabledAsync(false, token);
        File.Exists(_flag).Should().BeTrue();
        (await flag.IsEnabledAsync(token)).Should().BeFalse();
        await flag.SetEnabledAsync(true, token);
        File.Exists(_flag).Should().BeFalse();
        await flag.SetEnabledAsync(true, token);
    }

    [Theory]
    [InlineData(null, null, "flag", true)]
    [InlineData("/subscriptions/1/resourceGroups/rg", "logic-driver-reports", null, true)]
    [InlineData(null, null, null, false)]
    [InlineData("/subscriptions/1/resourceGroups/rg", null, null, false)]
    [InlineData("/subscriptions/1/resourceGroups/rg", "logic", "flag", false)]
    public void TheOptions_NeedEitherTheAzureSiteOrTheLocalFlag(string? resourceGroup, string? name, string? flag, bool valid) =>
        new FaultLogicAppOptions { ResourceGroupId = resourceGroup, Name = name, FlagPath = flag }.IsValid.Should().Be(valid);
}
