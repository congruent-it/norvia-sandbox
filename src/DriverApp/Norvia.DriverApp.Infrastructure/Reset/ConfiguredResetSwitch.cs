using Microsoft.Extensions.Options;
using Norvia.DriverApp.Application.Reset;

namespace Norvia.DriverApp.Infrastructure.Reset;

internal sealed class ConfiguredResetSwitch(IOptions<ResetOptions> options) : IResetSwitch
{
    public bool IsOn => options.Value.Enabled;
}
