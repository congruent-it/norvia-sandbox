using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Reset;

namespace Norvia.PartnerPortal.Infrastructure.Reset;

internal sealed class ConfiguredResetSwitch(IOptions<ResetOptions> options) : IResetSwitch
{
    public bool IsOn => options.Value.Enabled;
}
