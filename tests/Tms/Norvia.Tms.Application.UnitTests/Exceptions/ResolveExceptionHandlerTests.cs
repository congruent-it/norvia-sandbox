using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions.Resolve;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class ResolveExceptionHandlerTests
{
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly ResolveExceptionHandler _handler;

    public ResolveExceptionHandlerTests()
    {
        _handler = new ResolveExceptionHandler(_exceptions, _time);
    }

    [Fact]
    public async Task Handle_AnOpenException_ResolvesItWithTheOutcomeAndRemark_AndSavesOnce()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.ClearDomainEvents();
        Found(exception);

        var result = await _handler.Handle(Resolve(), CancellationToken.None);

        result.Value.Should().Be(new ResolveExceptionResult("EX-0000005", "SH-0000012", ExceptionStatus.Resolved, ExceptionOutcome.Redelivered, "corr-resolve"));
        exception.Outcome.Should().Be(ExceptionOutcome.Redelivered);
        exception.Remark.Should().Be("Redelivery Thursday");
        exception.ResolvedAt.Should().Be(Samples.Now.AddDays(3));
        exception.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionResolvedEvent>();
        await _exceptions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("EX-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_AnUnknownException_IsRefused_WithExceptionNotFound(string number)
    {
        var result = await _handler.Handle(Resolve(number), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotFound);
    }

    [Fact]
    public async Task Handle_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("corr-1"), Samples.Now.AddDays(2));
        Found(exception);

        var result = await _handler.Handle(Resolve(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotOpen);
        exception.Outcome.Should().Be(ExceptionOutcome.NoActionNeeded);
    }

    private async Task AssertRefused(Result<ResolveExceptionResult> result, ExceptionRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void Found(ExceptionCase exception) =>
        _exceptions.SingleOrDefaultAsync(Arg.Is<ExceptionToResolveSpec>(s => s.Number == exception.Number), Arg.Any<CancellationToken>()).Returns(exception);

    private static ResolveExceptionCommand Resolve(string number = "EX-0000005") =>
        new(number, ExceptionOutcome.Redelivered, "Redelivery Thursday", "corr-resolve");
}
