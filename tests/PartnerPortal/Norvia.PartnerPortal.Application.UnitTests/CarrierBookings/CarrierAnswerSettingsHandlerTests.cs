using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierBookings;

public sealed class CarrierAnswerSettingsHandlerTests
{
    private readonly IRepository<CarrierAnswerSettings> _repository = Substitute.For<IRepository<CarrierAnswerSettings>>();
    private readonly IReadRepository<CarrierAnswerSettings> _readRepository = Substitute.For<IReadRepository<CarrierAnswerSettings>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();

    public CarrierAnswerSettingsHandlerTests()
    {
        _personas.Find(Samples.KesslerSpedition.Id).Returns(Samples.KesslerSpedition);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
    }

    private ValueTask<Result<CarrierAnswerSettingsView>> Get(string persona) =>
        new GetCarrierAnswerSettingsHandler(_readRepository, _personas).Handle(new GetCarrierAnswerSettingsQuery(persona), CancellationToken.None);

    private ValueTask<Result<CarrierAnswerSettingsView>> Change(string persona, CarrierAnswerMode mode, int delaySeconds, CarrierRejectionReason reason = CarrierRejectionReason.NoCapacity) =>
        new ChangeCarrierAnswerSettingsHandler(_repository, _personas).Handle(new ChangeCarrierAnswerSettingsCommand(persona, mode, reason, delaySeconds), CancellationToken.None);

    [Fact]
    public async Task Get_ForAPersonaNeverChanged_IsTheSeedFilesDefault()
    {
        var result = await Get("kessler-spedition");

        result.Value.Should().Be(new CarrierAnswerSettingsView(CarrierAnswerMode.AutoConfirm, CarrierRejectionReason.NoCapacity, 5));
    }

    [Fact]
    public async Task Get_ForAPersonaThePresenterChanged_IsWhatTheyChose()
    {
        _readRepository.FirstOrDefaultAsync(Arg.Any<CarrierAnswerSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns(
            CarrierAnswerSettings.Choose(Samples.KesslerSpedition.Id, new CarrierAnswerPolicy(CarrierAnswerMode.AutoReject, CarrierRejectionReason.GoodsNotAccepted, TimeSpan.FromSeconds(9))));

        var result = await Get("kessler-spedition");

        result.Value.Should().Be(new CarrierAnswerSettingsView(CarrierAnswerMode.AutoReject, CarrierRejectionReason.GoodsNotAccepted, 9));
    }

    [Theory]
    [InlineData("brouwer-dranken")]
    [InlineData("nobody")]
    public async Task Get_ForNoCarrierPersona_IsNotFound(string persona) =>
        (await Get(persona)).Status.Should().Be(ResultStatus.NotFound);

    [Fact]
    public async Task Change_ForAPersonaNeverChanged_KeepsTheChoice()
    {
        var result = await Change("kessler-spedition", CarrierAnswerMode.AutoReject, 3, CarrierRejectionReason.LaneNotServed);

        result.Value.Should().Be(new CarrierAnswerSettingsView(CarrierAnswerMode.AutoReject, CarrierRejectionReason.LaneNotServed, 3));
        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierAnswerSettings>(settings => settings.PersonaId == Samples.KesslerSpedition.Id && settings.Policy.Mode == CarrierAnswerMode.AutoReject && settings.Policy.Delay == TimeSpan.FromSeconds(3)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Change_ForAPersonaChangedBefore_ReplacesTheChoice()
    {
        var chosen = CarrierAnswerSettings.Choose(Samples.KesslerSpedition.Id, new CarrierAnswerPolicy(CarrierAnswerMode.Manual, CarrierRejectionReason.NoCapacity, TimeSpan.Zero));
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierAnswerSettingsOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns(chosen);

        await Change("kessler-spedition", CarrierAnswerMode.AutoConfirm, 7);

        chosen.Policy.Mode.Should().Be(CarrierAnswerMode.AutoConfirm);
        chosen.Policy.Delay.Should().Be(TimeSpan.FromSeconds(7));
        await _repository.Received(1).UpdateAsync(chosen, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierAnswerSettings>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task Change_WithADelayOutOfRange_IsInvalid_AndChangesNothing(int seconds)
    {
        var result = await Change("kessler-spedition", CarrierAnswerMode.AutoConfirm, seconds);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierAnswerSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Change_WithAnUnknownMode_IsInvalid() =>
        (await Change("kessler-spedition", (CarrierAnswerMode)42, 3)).Status.Should().Be(ResultStatus.Invalid);

    [Fact]
    public async Task Change_ForNoCarrierPersona_IsNotFound() =>
        (await Change("brouwer-dranken", CarrierAnswerMode.AutoConfirm, 3)).Status.Should().Be(ResultStatus.NotFound);
}
