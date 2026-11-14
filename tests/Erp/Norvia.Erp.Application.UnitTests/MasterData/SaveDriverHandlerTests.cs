using Ardalis.Result;
using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Drivers;
using Norvia.Erp.Application.MasterData.Drivers.Save;
using Norvia.Erp.Domain.Drivers;

namespace Norvia.Erp.Application.UnitTests.MasterData;

public sealed class SaveDriverHandlerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly IRepository<Driver> _drivers = Substitute.For<IRepository<Driver>>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private SaveDriverHandler Handler => new(_drivers, _outbox, _time, NullLogger<SaveDriverHandler>.Instance);

    private void Have(Driver driver) =>
        _drivers.FirstOrDefaultAsync(Arg.Any<ISpecification<Driver>>(), Arg.Any<CancellationToken>()).Returns(driver);

    private static Driver Anouk() => new(new DriverId("DRV-0001"), "Anouk de Vries", [LicenceCategory.B, LicenceCategory.C], true);

    [Fact]
    public async Task Handle_ForANewDriver_AddsItAtRevisionOneAndPublishesTheWholeRecord()
    {
        var result = await Handler.Handle(new SaveDriverCommand("DRV-0100", "Nieuw", ["CE", "B"], true), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Created);
        result.Value.Driver.Should().BeEquivalentTo(new DriverView("DRV-0100", "Nieuw", ["B", "CE"], true, 1));
        await _drivers.Received().AddAsync(Arg.Is<Driver>(driver => driver.Id.Value == "DRV-0100" && driver.FinanceOwned), Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e =>
            e is DriverChangedV1 && e.EventType == "DriverChanged" && e.Topic == Topics.MasterDataEvents && ((DriverChangedV1)e).Driver.Revision == 1));
    }

    [Fact]
    public async Task Handle_MakingADriverInactive_StoresOnlyTheFlagAndPublishesTheRecord()
    {
        var driver = Anouk();
        Have(driver);

        var result = await Handler.Handle(new SaveDriverCommand("DRV-0001", "Anouk de Vries", ["C", "B"], false), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Updated);
        result.Value.Driver.Revision.Should().Be(2);
        result.Value.Driver.Active.Should().BeFalse();
        driver.Licences.Should().Equal(LicenceCategory.B, LicenceCategory.C);
        await _drivers.Received().UpdateAsync(driver, Arg.Any<CancellationToken>());
        _outbox.Received().Add(Arg.Is<IntegrationEvent>(e => !((DriverChangedV1)e).Driver.Active));
    }

    [Fact]
    public async Task Handle_ForTheSameContentWithLicencesInAnotherOrder_ChangesAndPublishesNothing()
    {
        Have(Anouk());

        var result = await Handler.Handle(new SaveDriverCommand("DRV-0001", "Anouk de Vries", ["C", "B"], true), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Unchanged);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _drivers.DidNotReceive().UpdateAsync(Arg.Any<Driver>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("DRV-1", " ")]
    public async Task Handle_WithoutAnIdOrAName_IsRefusedAsRequired(string id, string name)
    {
        var result = await Handler.Handle(new SaveDriverCommand(id, name, [], true), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldRequired");
    }

    [Fact]
    public async Task Handle_WithATooLongName_IsRefusedAsTooLong()
    {
        var result = await Handler.Handle(new SaveDriverCommand("DRV-1", new string('x', 201), [], true), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldTooLong");
    }

    [Theory]
    [InlineData("D")]
    [InlineData("b")]
    [InlineData("9")]
    public async Task Handle_WithAnUnknownLicence_IsRefusedAsInvalid(string licence)
    {
        var result = await Handler.Handle(new SaveDriverCommand("DRV-1", "Name", ["B", licence], true), CancellationToken.None);

        result.ValidationErrors.Single().ErrorCode.Should().Be("MasterDataFieldInvalid");
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithoutLicences_SavesADriverWhoDrivesNothing()
    {
        var result = await Handler.Handle(new SaveDriverCommand("DRV-1", "Name", null!, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Driver.Licences.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ForASaveSentAgainUnderTheSameKey_AnswersAsRepeatAndPublishesNothing()
    {
        var record = Anouk();
        Have(record);
        await Handler.Handle(new SaveDriverCommand("DRV-0001", "Anouk de Vries", ["B", "C"], false, "key-1"), CancellationToken.None);
        _outbox.ClearReceivedCalls();
        _drivers.ClearReceivedCalls();

        var result = await Handler.Handle(new SaveDriverCommand("DRV-0001", "Anouk de Vries", ["B", "C"], false, "key-1"), CancellationToken.None);

        result.Value.Outcome.Should().Be(MasterDataSaveOutcome.Repeat);
        record.Revision.Should().Be(2);
        _outbox.DidNotReceive().Add(Arg.Any<IntegrationEvent>());
        await _drivers.DidNotReceive().UpdateAsync(Arg.Any<Driver>(), Arg.Any<CancellationToken>());
    }
}
