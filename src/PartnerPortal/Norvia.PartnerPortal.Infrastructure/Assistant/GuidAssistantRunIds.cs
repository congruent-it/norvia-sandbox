using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

internal sealed class GuidAssistantRunIds : IAssistantRunIds
{
    public string NewRunId() => Guid.NewGuid().ToString("N");
}
