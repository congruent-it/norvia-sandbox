namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>Gives each Agent run its ID, so tests can fix it.</summary>
public interface IAssistantRunIds
{
    string NewRunId();
}
