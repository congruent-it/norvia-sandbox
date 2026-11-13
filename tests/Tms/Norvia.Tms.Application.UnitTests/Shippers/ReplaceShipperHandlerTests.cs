using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.Shippers.Replace;
using Norvia.Tms.Domain.Shippers;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shippers;

public sealed class ReplaceShipperHandlerTests
{
    private readonly IShipperDirectory _directory = Substitute.For<IShipperDirectory>();
    private readonly IShipperReplicas _replicas = Substitute.For<IShipperReplicas>();

    private ReplaceShipperHandler Handler => new(_directory, _replicas, NullLogger<ReplaceShipperHandler>.Instance);

    [Fact]
    public async Task Handle_ANewShipper_IsKeptWholeAtItsRevision()
    {
        var result = await Handler.Handle(new ReplaceShipperCommand("SHP-0100", "Nieuw", true, 1), CancellationToken.None);

        result.Value.Should().Be(new ReplaceShipperResult(Applied: true, 1));
        await _replicas.Received().ReplaceAsync(new Shipper(new ShipperId("SHP-0100"), "Nieuw", true, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AHigherRevision_ReplacesTheCopy()
    {
        _directory.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Old", true));

        var result = await Handler.Handle(new ReplaceShipperCommand("SHP-0001", "New", false, 2), CancellationToken.None);

        result.Value.Should().Be(new ReplaceShipperResult(Applied: true, 2));
        await _replicas.Received().ReplaceAsync(new Shipper(new ShipperId("SHP-0001"), "New", false, 2), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Handle_ARevisionThatIsNotHigher_ChangesNothingAndStillSucceeds(int revision)
    {
        _directory.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Held", true, 2));

        var result = await Handler.Handle(new ReplaceShipperCommand("SHP-0001", "Late", false, revision), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReplaceShipperResult(Applied: false, 2));
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Shipper>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Name", 1)]
    [InlineData("SHP-1", " ", 1)]
    [InlineData("SHP-1", "Name", 0)]
    public async Task Handle_AnUnreadableRecord_IsRefusedWithMasterDataUnreadable(string id, string name, int revision)
    {
        var result = await Handler.Handle(new ReplaceShipperCommand(id, name, true, revision), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataUnreadable");
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Shipper>(), Arg.Any<CancellationToken>());
    }
}
