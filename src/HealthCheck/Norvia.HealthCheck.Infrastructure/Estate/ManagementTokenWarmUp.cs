using Azure.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Norvia.HealthCheck.Infrastructure.Estate;

/// <summary>
/// Asks for the app's token for Azure Resource Manager as the app starts, so the first health check after a deployment does not wait for it
/// (the managed identity's first token takes about 3 s; the next ones come from the cache). A failure is only logged: the run asks again.
/// </summary>
internal sealed partial class ManagementTokenWarmUp(TokenCredential credential, ILogger<ManagementTokenWarmUp> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await credential.GetTokenAsync(new TokenRequestContext(AzureEstateConfiguration.ManagementScopes), stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app's token for Azure Resource Manager could not be fetched at start; the first health check asks for it")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
