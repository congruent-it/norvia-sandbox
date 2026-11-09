using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Erp.Infrastructure.Data;

namespace Norvia.Erp.Infrastructure.UnitTests.Data;

public sealed class EfConflictRetrierTests
{
    private readonly IMediator _first = Substitute.For<IMediator>();
    private readonly IMediator _retried = Substitute.For<IMediator>();

    private EfConflictRetrier Retrier()
    {
        var services = new ServiceCollection().AddScoped(_ => _retried).BuildServiceProvider();
        return new EfConflictRetrier(_first, services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task RunAsync_WhenTheSaveSucceeds_RunsTheCallOnce_OnTheRequestsMediator()
    {
        var used = new List<IMediator>();

        var result = await Retrier().RunAsync(mediator =>
        {
            used.Add(mediator);
            return Task.FromResult("stored");
        });

        result.Should().Be("stored");
        used.Should().Equal(_first);
    }

    [Fact]
    public async Task RunAsync_WhenTheSaveConflicts_RunsTheCallAgain_OnAMediatorOfANewScope()
    {
        var used = new List<IMediator>();

        var result = await Retrier().RunAsync(mediator =>
        {
            used.Add(mediator);
            return used.Count == 1 ? throw new DbUpdateConcurrencyException("Lost the row version.") : Task.FromResult("repeated");
        });

        result.Should().Be("repeated");
        used.Should().Equal(_first, _retried);
    }

    [Fact]
    public async Task RunAsync_WhenTheRetryConflictsToo_RunsTheCallOnceMore()
    {
        var calls = 0;

        var result = await Retrier().RunAsync(_ =>
        {
            calls++;
            return calls <= 2 ? throw new DbUpdateConcurrencyException("Lost the row version.") : Task.FromResult("third time");
        });

        result.Should().Be("third time");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_WhenEveryRunConflicts_GivesUpAfterTheFirstRunAndThreeRetries_AndThrowsTheLastConflict()
    {
        var calls = 0;

        var run = () => Retrier().RunAsync<string>(_ =>
        {
            calls++;
            throw new DbUpdateConcurrencyException($"Conflict {calls}.");
        });

        (await run.Should().ThrowAsync<DbUpdateConcurrencyException>()).WithMessage("Conflict 4.");
        calls.Should().Be(4);
    }

    [Fact]
    public async Task RunAsync_ForAFailureThatIsNoSaveConflict_IsNotRetried()
    {
        var calls = 0;

        var run = () => Retrier().RunAsync<string>(_ =>
        {
            calls++;
            throw new InvalidOperationException("A bug.");
        });

        await run.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ForAFailureOfTheRetryThatIsNoSaveConflict_StopsThere()
    {
        var calls = 0;

        var run = () => Retrier().RunAsync<string>(_ =>
        {
            calls++;
            throw calls == 1 ? new DbUpdateConcurrencyException("Lost the row version.") : new InvalidOperationException("A bug.");
        });

        await run.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ForADbUpdateExceptionThatIsNoDuplicateKey_IsNotRetried()
    {
        var calls = 0;

        var run = () => Retrier().RunAsync<string>(_ =>
        {
            calls++;
            throw new DbUpdateException("The database is down.");
        });

        await run.Should().ThrowAsync<DbUpdateException>();
        calls.Should().Be(1);
    }
}
