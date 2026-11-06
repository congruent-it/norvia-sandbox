using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Plans;

public sealed class GetConfigureHandlerTests
{
    private static IWorldSource World()
    {
        var world = Substitute.For<IWorldSource>();
        world.Load().Returns(TestData.FleetWorld);
        return world;
    }

    [Fact]
    public async Task Handle_OffersThePresetsTheLimitsAndANewSeed()
    {
        var sequences = Substitute.For<IReadRepository<RunSequence>>();
        var seeds = Substitute.For<ISeedSource>();
        seeds.NewSeed().Returns(4242);

        var view = await new GetConfigureHandler(sequences, seeds, TestData.Limits, World()).Handle(new GetConfigureQuery(), CancellationToken.None);

        view.Presets.Should().BeEquivalentTo(Presets.All);
        view.Limits.Should().Be(TestData.Limits);
        view.NewSeed.Should().Be(4242);
        view.NextRunNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ARunNumberAlreadyUsed_OffersTheNext()
    {
        var sequence = RunSequence.Create();
        sequence.Take();
        var sequences = Substitute.For<IReadRepository<RunSequence>>();
        sequences.FirstOrDefaultAsync(Arg.Any<ISpecification<RunSequence>>(), Arg.Any<CancellationToken>()).Returns(sequence);

        var view = await new GetConfigureHandler(sequences, Substitute.For<ISeedSource>(), TestData.Limits, World()).Handle(new GetConfigureQuery(), CancellationToken.None);

        view.NextRunNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OffersTheCarriersWithAPersonaAndTheDriversOfTheOwnFleetForTheHostileProfile()
    {
        var view = await new GetConfigureHandler(Substitute.For<IReadRepository<RunSequence>>(), Substitute.For<ISeedSource>(), TestData.Limits, World())
            .Handle(new GetConfigureQuery(), CancellationToken.None);

        view.HostileCarriers.Select(choice => choice.Id).Should().Equal("CAR-0001", "CAR-0002");
        view.HostileDrivers.Select(choice => choice.Id).Should().Equal(TestData.Anouk.Id, TestData.Bram.Id, TestData.Sanne.Id);
        view.HostileDrivers[0].Name.Should().Be("Anouk de Vries");
    }
}
