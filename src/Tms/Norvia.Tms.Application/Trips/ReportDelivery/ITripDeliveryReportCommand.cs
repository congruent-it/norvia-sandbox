namespace Norvia.Tms.Application.Trips.ReportDelivery;

/// <summary>A report of the Trip dispatch Logic App about one Trip version.</summary>
public interface ITripDeliveryReportCommand
{
    string Number { get; }

    int Version { get; }

    string CorrelationId { get; }
}
