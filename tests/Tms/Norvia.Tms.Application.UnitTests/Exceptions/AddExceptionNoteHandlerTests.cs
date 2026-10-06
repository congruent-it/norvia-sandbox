using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions.AddNote;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class AddExceptionNoteHandlerTests
{
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly AddExceptionNoteHandler _handler;

    public AddExceptionNoteHandlerTests()
    {
        _handler = new AddExceptionNoteHandler(_exceptions, _time);
    }

    [Fact]
    public async Task Handle_AnOpenException_RecordsTheNoteWithThePlannerAndTime_PublishesNothing_AndSavesOnce()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.ClearDomainEvents();
        Found(exception);

        var result = await _handler.Handle(Add(), CancellationToken.None);

        result.Value.Should().Be(new AddExceptionNoteResult("EX-0000005", "SH-0000012", 1, "corr-note"));
        exception.Notes.Should().ContainSingle().Which.Should().Be(
            new ExceptionNote("Phoned Kessler", "Anna de Vries", new CorrelationId("corr-note"), Samples.Now.AddDays(3)));
        exception.Status.Should().Be(ExceptionStatus.Open);
        exception.DomainEvents.Should().BeEmpty();
        await _exceptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("EX-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_AnUnknownException_IsRefused_WithExceptionNotFound(string number)
    {
        var result = await _handler.Handle(Add(number), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotFound);
    }

    [Fact]
    public async Task Handle_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("corr-1"), Samples.Now.AddDays(2));
        Found(exception);

        var result = await _handler.Handle(Add(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotOpen);
        exception.Notes.Should().BeEmpty();
    }

    private async Task AssertRefused(Result<AddExceptionNoteResult> result, ExceptionRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void Found(ExceptionCase exception) =>
        _exceptions.SingleOrDefaultAsync(Arg.Is<ExceptionToResolveSpec>(s => s.Number == exception.Number), Arg.Any<CancellationToken>()).Returns(exception);

    private static AddExceptionNoteCommand Add(string number = "EX-0000005") => new(number, "Phoned Kessler", "Anna de Vries", "corr-note");
}
