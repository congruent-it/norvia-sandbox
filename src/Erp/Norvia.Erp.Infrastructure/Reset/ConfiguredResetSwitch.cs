using Microsoft.Extensions.Options;
using Norvia.Erp.Application.Reset;

namespace Norvia.Erp.Infrastructure.Reset;

internal sealed class ConfiguredResetSwitch(IOptions<ResetOptions> options) : IResetSwitch
{
    public bool IsOn => options.Value.Enabled;
}
