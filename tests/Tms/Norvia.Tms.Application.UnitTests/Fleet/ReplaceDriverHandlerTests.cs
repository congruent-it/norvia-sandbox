using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.ReplaceDriver;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet;

public sealed class ReplaceDriverHandlerTests
{
    private static readonly DriverId Id = new("DRV-0001");
    private readonly IDriverDirectory _directory = Substitute.For<IDriverDirectory>();
    private readonly IDriverReplicas _replicas = Substitute.For<IDriverReplicas>();

    private ReplaceDriverHandler Handler => new(_directory, _replicas, NullLogger<ReplaceDriverHandler>.Instance);

    private static Driver Held(int revision = 1) => new(Id, "Anouk", new HashSet<LicenceCategory> { LicenceCategory.B }, true, revision);

    [Fact]
    public async Task Handle_ANewDriver_IsKeptWholeAtItsRevision()
    {
        var result = await Handler.Handle(new ReplaceDriverCommand("DRV-0100", "Nieuw", ["B", "CE"], true, 1), CancellationToken.None);

        result.Value.Should().Be(new ReplaceDriverResult(Applied: true, 1));
        await _replicas.Received().ReplaceAsync(
            Arg.Is<Driver>(driver => driver.Id.Value == "DRV-0100" && driver.Name == "Nieuw" && driver.Active && driver.Revision == 1
                && driver.Licences.SetEquals(new[] { LicenceCategory.B, LicenceCategory.CE })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AHigherRevisionThatMakesTheDriverInactive_ReplacesTheCopy()
    {
        _directory.Find(Id).Returns(Held());

        var result = await Handler.Handle(new ReplaceDriverCommand("DRV-0001", "Anouk", ["B"], false, 2), CancellationToken.None);

        result.Value.Should().Be(new ReplaceDriverResult(Applied: true, 2));
        await _replicas.Received().ReplaceAsync(Arg.Is<Driver>(driver => !driver.Active && driver.Revision == 2 && driver.Licences.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADriverWithoutLicences_IsKept()
    {
        var result = await Handler.Handle(new ReplaceDriverCommand("DRV-0001", "Anouk", [], true, 1), CancellationToken.None);

        result.Value.Applied.Should().BeTrue();
        await _replicas.Received().ReplaceAsync(Arg.Is<Driver>(driver => driver.Licences.Count == 0), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Handle_ARevisionThatIsNotHigher_ChangesNothingAndStillSucceeds(int revision)
    {
        _directory.Find(Id).Returns(Held(2));

        var result = await Handler.Handle(new ReplaceDriverCommand("DRV-0001", "Late", ["C"], false, revision), CancellationToken.None);

        result.Value.Should().Be(new ReplaceDriverResult(Applied: false, 2));
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Driver>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Name", "B", 1)]
    [InlineData("DRV-1", " ", "B", 1)]
    [InlineData("DRV-1", "Name", "D", 1)]
    [InlineData("DRV-1", "Name", "ce", 1)]
    [InlineData("DRV-1", "Name", "B", 0)]
    public async Task Handle_AnUnreadableRecord_IsRefusedWithMasterDataUnreadable(string id, string name, string licence, int revision)
    {
        var result = await Handler.Handle(new ReplaceDriverCommand(id, name, [licence], true, revision), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataUnreadable");
        await _replicas.DidNotReceive().ReplaceAsync(Arg.Any<Driver>(), Arg.Any<CancellationToken>());
    }
}
