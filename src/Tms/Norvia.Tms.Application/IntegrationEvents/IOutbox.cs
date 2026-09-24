namespace Norvia.Tms.Application.IntegrationEvents;

/// <summary>
/// Queues an integration event to be published once, and only if, the change that caused it is committed.
/// </summary>
public interface IOutbox
{
    void Add(IntegrationEvent integrationEvent);
}
