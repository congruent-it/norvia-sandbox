using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierShipments;

public sealed class CarrierTrackingSettingsHandlerTests
{
    private readonly IRepository<CarrierTrackingSettings> _repository = Substitute.For<IRepository<CarrierTrackingSettings>>();
    private readonly IReadRepository<CarrierTrackingSettings> _readRepository = Substitute.For<IReadRepository<CarrierTrackingSettings>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();

    public CarrierTrackingSettingsHandlerTests()
    {
        _personas.Find(Samples.KesslerSpedition.Id).Returns(Samples.KesslerSpedition);
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
    }

    private ValueTask<Result<CarrierTrackingSettingsView>> Get(string persona) =>
        new GetCarrierTrackingSettingsHandler(_readRepository, _personas).Handle(new GetCarrierTrackingSettingsQuery(persona), CancellationToken.None);

    private ValueTask<Result<CarrierTrackingSettingsView>> Change(string persona, TrackingMode mode, int delaySeconds) =>
        new ChangeCarrierTrackingSettingsHandler(_repository, _personas).Handle(new ChangeCarrierTrackingSettingsCommand(persona, mode, delaySeconds), CancellationToken.None);

    [Fact]
    public async Task Get_ForAPersonaNeverChanged_IsTheSeedFilesDefault()
    {
        (await Get("kessler-spedition")).Value.Should().Be(new CarrierTrackingSettingsView(TrackingMode.AutoProgress, 5));
        (await Get("van-dijk-transport")).Value.Should().Be(new CarrierTrackingSettingsView(TrackingMode.Manual, 5));
    }

    [Fact]
    public async Task Get_ForAPersonaThePresenterChanged_IsWhatTheyChose()
    {
        _readRepository.FirstOrDefaultAsync(Arg.Any<CarrierTrackingSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>())
            .Returns(CarrierTrackingSettings.Choose(Samples.KesslerSpedition.Id, new CarrierTrackingPolicy(TrackingMode.Manual, TimeSpan.FromSeconds(9))));

        (await Get("kessler-spedition")).Value.Should().Be(new CarrierTrackingSettingsView(TrackingMode.Manual, 9));
    }

    [Theory]
    [InlineData("brouwer-dranken")]
    [InlineData("nobody")]
    public async Task Get_ForNoCarrierPersona_IsNotFound(string persona) =>
        (await Get(persona)).Status.Should().Be(ResultStatus.NotFound);

    [Fact]
    public async Task Change_ForAPersonaNeverChanged_KeepsTheChoice()
    {
        var result = await Change("van-dijk-transport", TrackingMode.AutoProgress, 3);

        result.Value.Should().Be(new CarrierTrackingSettingsView(TrackingMode.AutoProgress, 3));
        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierTrackingSettings>(settings => settings.PersonaId == Samples.VanDijkTransport.Id && settings.Policy.Mode == TrackingMode.AutoProgress && settings.Policy.Delay == TimeSpan.FromSeconds(3)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Change_ForAPersonaChangedBefore_ReplacesTheChoice()
    {
        var chosen = CarrierTrackingSettings.Choose(Samples.KesslerSpedition.Id, new CarrierTrackingPolicy(TrackingMode.Manual, TimeSpan.Zero));
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierTrackingSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns(chosen);

        await Change("kessler-spedition", TrackingMode.AutoProgress, 7);

        chosen.Policy.Mode.Should().Be(TrackingMode.AutoProgress);
        chosen.Policy.Delay.Should().Be(TimeSpan.FromSeconds(7));
        await _repository.Received(1).UpdateAsync(chosen, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierTrackingSettings>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task Change_WithADelayOutOfRange_IsInvalid_AndChangesNothing(int seconds)
    {
        var result = await Change("kessler-spedition", TrackingMode.AutoProgress, seconds);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierTrackingSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Change_WithAnUnknownMode_IsInvalid() =>
        (await Change("kessler-spedition", (TrackingMode)42, 3)).Status.Should().Be(ResultStatus.Invalid);

    [Fact]
    public async Task Change_ForNoCarrierPersona_IsNotFound() =>
        (await Change("brouwer-dranken", TrackingMode.AutoProgress, 3)).Status.Should().Be(ResultStatus.NotFound);
}
