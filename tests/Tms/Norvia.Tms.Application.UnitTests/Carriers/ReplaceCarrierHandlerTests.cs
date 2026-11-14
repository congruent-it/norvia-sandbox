using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Carriers.Replace;
using Norvia.Tms.Domain.Carriers;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Carriers;

public sealed class ReplaceCarrierHandlerTests
{
    private readonly ICarrierDirectory _directory = Substitute.For<ICarrierDirectory>();
    private readonly ICarrierReplicas _replicas = Substitute.For<ICarrierReplicas>();

    private ReplaceCarrierHandler Handler => new(_directory, _replicas, NullLogger<ReplaceCarrierHandler>.Instance);

    [Fact]
    public async Task Handle_ANewCarrier_IsKeptWholeAtItsRevision()
    {
        var result = await Handler.Handle(new ReplaceCarrierCommand("CAR-0100", "Nieuw", true, 1), CancellationToken.None);

        result.Value.Should().Be(new ReplaceCarrierResult(Applied: true, 1));
        await _replicas.Received().ReplaceAsync(new Carrier(new CarrierId("CAR-0100"), "Nieuw", true, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AHigherRevision_ReplacesTheCopy()
    {
        _directory.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Old", true));

        var result = await Handler.Handle(new ReplaceCarrierCommand("CAR-0001", "New", false, 2), CancellationToken.None);

        result.Value.Should().Be(new ReplaceCarrierResult(Applied: true, 2));
        await _replicas.Received().ReplaceAsync(new Carrier(new CarrierId("CAR-0001"), "New", false, 2), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Handle_ARevisionThatIsNotHigher_ChangesNothingAndStillSucceeds(int revision)
    {
        _directory.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Held", true, 2));

        var result = await Handler.Handle(new ReplaceCarrierCommand("CAR-0001", "Late", false, revision), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReplaceCarrierResult(Applied: false, 2));
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Carrier>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Name", 1)]
    [InlineData("CAR-1", " ", 1)]
    [InlineData("CAR-1", "Name", 0)]
    public async Task Handle_AnUnreadableRecord_IsRefusedWithMasterDataUnreadable(string id, string name, int revision)
    {
        var result = await Handler.Handle(new ReplaceCarrierCommand(id, name, true, revision), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataUnreadable");
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Carrier>(), Arg.Any<CancellationToken>());
    }
}
