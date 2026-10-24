using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>Locally the Logic App is a worker, which stops while the flag file exists.</summary>
internal sealed class FlagFileLogicAppSwitch(IOptions<FaultsOptions> options) : ILogicAppSwitch
{
    private readonly string _flag = options.Value.LogicApp.FlagPath ?? throw new InvalidOperationException("Faults:LogicApp:FlagPath isn't configured.");

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(!File.Exists(_flag));

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            File.Delete(_flag);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_flag))!);
            File.WriteAllText(_flag, string.Empty);
        }

        return Task.CompletedTask;
    }
}
