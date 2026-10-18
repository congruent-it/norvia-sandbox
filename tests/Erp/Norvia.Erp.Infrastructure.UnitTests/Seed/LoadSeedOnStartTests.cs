using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.Erp.Application.MasterData.Load;
using Norvia.Erp.Infrastructure.Seed;

namespace Norvia.Erp.Infrastructure.UnitTests.Seed;

public sealed class LoadSeedOnStartTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"erp-seed-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public async Task Start_ReadsTheSeedFileAndLoadsItsMasterData()
    {
        await File.WriteAllTextAsync(_path, """{ "shippers": [ { "id": "SHP-1", "name": "Shipper One", "active": true } ] }""", TestContext.Current.CancellationToken);
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        mediator.Send(Arg.Any<LoadMasterDataCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new LoadMasterDataResult(1, 0)));
#pragma warning restore CA2012
        await using var services = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();

        await new LoadSeedOnStart(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SeedOptions { Path = _path }), NullLogger<LoadSeedOnStart>.Instance)
            .StartAsync(CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<LoadMasterDataCommand>(command => command.Seed.Shippers.Single().Id == "SHP-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_WithABrokenSeedFile_StopsTheStart()
    {
        await File.WriteAllTextAsync(_path, "null", TestContext.Current.CancellationToken);
        await using var services = new ServiceCollection().AddSingleton(Substitute.For<IMediator>()).BuildServiceProvider();
        var loader = new LoadSeedOnStart(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SeedOptions { Path = _path }), NullLogger<LoadSeedOnStart>.Instance);

        var starting = async () => await loader.StartAsync(CancellationToken.None);

        await starting.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Stop_DoesNothing()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var loader = new LoadSeedOnStart(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SeedOptions { Path = _path }), NullLogger<LoadSeedOnStart>.Instance);

        await loader.StopAsync(CancellationToken.None);
    }
}
