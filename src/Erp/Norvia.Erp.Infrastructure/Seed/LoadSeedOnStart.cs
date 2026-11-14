using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.Erp.Application.MasterData.Load;

namespace Norvia.Erp.Infrastructure.Seed;

/// <summary>
/// Loads the Shippers, Carriers, Rate Cards, Carrier Rate Cards, Vehicles and Drivers from the seed file as the ERP starts, so the ERP has them to show.
/// A second start reloads what is there instead of adding it again. A broken seed stops the ERP here.
/// </summary>
internal sealed partial class LoadSeedOnStart(IServiceScopeFactory scopes, IOptions<SeedOptions> options, ILogger<LoadSeedOnStart> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.Path;
        MasterDataSeed seed;
        await using (var file = File.OpenRead(path))
        {
            seed = SeedFileReader.Read(file, path);
        }

        await using var scope = scopes.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new LoadMasterDataCommand(seed), cancellationToken);
        LogLoaded(result.Added, result.Reloaded);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded the seed file's master data: {Added} added, {Reloaded} reloaded")]
    private partial void LogLoaded(int added, int reloaded);
}
