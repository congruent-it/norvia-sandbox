using Ardalis.SharedKernel;
using Ardalis.Specification;
using Norvia.Simulator.Application.Configure;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.UnitTests.Plans;

public sealed class GetConfigureHandlerTests
{
    [Fact]
    public async Task Handle_OffersThePresetsTheLimitsAndANewSeed()
    {
        var sequences = Substitute.For<IReadRepository<RunSequence>>();
        var seeds = Substitute.For<ISeedSource>();
        seeds.NewSeed().Returns(4242);

        var view = await new GetConfigureHandler(sequences, seeds, TestData.Limits).Handle(new GetConfigureQuery(), CancellationToken.None);

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

        var view = await new GetConfigureHandler(sequences, Substitute.For<ISeedSource>(), TestData.Limits).Handle(new GetConfigureQuery(), CancellationToken.None);

        view.NextRunNumber.Should().Be(2);
    }
}
