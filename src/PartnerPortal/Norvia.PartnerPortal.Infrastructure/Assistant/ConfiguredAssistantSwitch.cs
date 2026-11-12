using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

internal sealed class ConfiguredAssistantSwitch(IOptions<AssistantOptions> options) : IAssistantSwitch
{
    public bool Enabled => options.Value.Enabled;
}
