using Ardalis.SharedKernel;
using Norvia.DriverApp.Application.Settings.GetCurrent;
using Norvia.DriverApp.Application.Settings.SetUnavailable;
using Norvia.DriverApp.Domain.Settings;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Settings;

/// <summary>The "Driver app unavailable" demo fault (P11).</summary>
public sealed class SettingsHandlerTests
{
    private readonly IRepository<DriverAppSettings> _repository = Substitute.For<IRepository<DriverAppSettings>>();
    private readonly IReadRepository<DriverAppSettings> _readRepository = Substitute.For<IReadRepository<DriverAppSettings>>();

    [Fact]
    public async Task SettingTheFaultTheFirstTime_AddsTheSettings()
    {
        await new SetUnavailableHandler(_repository).Handle(new SetUnavailableCommand(true), CancellationToken.None);

        await _repository.Received(1).AddAsync(Arg.Is<DriverAppSettings>(settings => settings.Unavailable), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettingTheFaultAgain_UpdatesTheSettings()
    {
        var stored = new DriverAppSettings();
        _repository.GetByIdAsync(DriverAppSettings.SingleId, Arg.Any<CancellationToken>()).Returns(stored);

        await new SetUnavailableHandler(_repository).Handle(new SetUnavailableCommand(true), CancellationToken.None);

        stored.Unavailable.Should().BeTrue();
        await _repository.Received(1).UpdateAsync(stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheFault_IsOffUntilSet()
    {
        (await new GetSettingsHandler(_readRepository).Handle(new GetSettingsQuery(), CancellationToken.None)).Unavailable.Should().BeFalse();

        var stored = new DriverAppSettings();
        stored.SetUnavailable(true);
        _readRepository.GetByIdAsync(DriverAppSettings.SingleId, Arg.Any<CancellationToken>()).Returns(stored);

        (await new GetSettingsHandler(_readRepository).Handle(new GetSettingsQuery(), CancellationToken.None)).Unavailable.Should().BeTrue();
    }
}
