namespace Norvia.DriverApp.Application.Events;

/// <summary>
/// Queues a Driver app event to be published once, and only if, the change that caused it is committed (ADR 0003, ADR 0016): a report is
/// never lost between recording it and publishing it, and never published without being recorded.
/// </summary>
public interface IDriverEventOutbox
{
    void Add(DriverAppEvent driverAppEvent);
}
