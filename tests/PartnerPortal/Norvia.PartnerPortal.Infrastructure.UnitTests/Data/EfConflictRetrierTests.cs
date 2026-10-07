using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

public sealed class EfConflictRetrierTests
{
    private readonly IMediator _first = Substitute.For<IMediator>();
    private readonly IMediator _second = Substitute.For<IMediator>();

    private EfConflictRetrier Retrier()
    {
        var services = new ServiceCollection().AddScoped(_ => _second).BuildServiceProvider();
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
    public async Task RunAsync_WhenTheSaveConflicts_RunsTheCallOnceMore_OnAMediatorOfANewScope()
    {
        var used = new List<IMediator>();

        var result = await Retrier().RunAsync(mediator =>
        {
            used.Add(mediator);
            return used.Count == 1 ? throw new DbUpdateException("Duplicate key.") : Task.FromResult("repeated");
        });

        result.Should().Be("repeated");
        used.Should().Equal(_first, _second);
    }

    [Fact]
    public async Task RunAsync_WhenTheRetryFailsToo_Throws()
    {
        var run = () => Retrier().RunAsync<string>(_ => throw new DbUpdateException("The database is down."));

        await run.Should().ThrowAsync<DbUpdateException>();
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
}
