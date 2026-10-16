using Microsoft.Extensions.Options;
using Norvia.Tms.Application.Reset;

namespace Norvia.Tms.Infrastructure.Reset;

internal sealed class ConfiguredResetSwitch(IOptions<ResetOptions> options) : IResetSwitch
{
    public bool IsOn => options.Value.Enabled;
}
