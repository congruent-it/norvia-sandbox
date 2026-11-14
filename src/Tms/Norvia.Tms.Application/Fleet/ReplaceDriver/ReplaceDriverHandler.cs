using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.ReplaceDriver;

public sealed partial class ReplaceDriverHandler(IDriverDirectory directory, IDriverReplicas replicas, ILogger<ReplaceDriverHandler> logger)
    : ICommandHandler<ReplaceDriverCommand, Result<ReplaceDriverResult>>
{
    public async ValueTask<Result<ReplaceDriverResult>> Handle(ReplaceDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var licences = new HashSet<LicenceCategory>();
        var licencesReadable = true;
        foreach (var name in command.Licences ?? [])
        {
            if (Enum.TryParse<LicenceCategory>(name, ignoreCase: false, out var licence) && Enum.IsDefined(licence))
            {
                licences.Add(licence);
            }
            else
            {
                licencesReadable = false;
            }
        }

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name) || command.Revision < 1 || !licencesReadable)
        {
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = "MasterDataUnreadable" });
            return Result<ReplaceDriverResult>.Invalid(new ValidationError(
                "Driver",
                "A Driver needs an ID, a name, licences among B, C and CE, and a Master-data revision of at least 1.",
                "MasterDataUnreadable",
                ValidationSeverity.Error));
        }

        var id = new DriverId(command.Id);
        using var trail = logger.Track(new() { [TrackingKeys.PartyId] = command.Id });
        var current = directory.Find(id)?.Revision ?? 0;
        if (command.Revision <= current)
        {
            using var stale = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStale(logger, command.Id, command.Revision, current);
            return new ReplaceDriverResult(Applied: false, current);
        }

        await replicas.ReplaceAsync(new Driver(id, command.Name, licences, command.Active, command.Revision), cancellationToken);
        using var replaced = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        LogReplaced(logger, command.Id, command.Revision);
        return new ReplaceDriverResult(Applied: true, command.Revision);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Driver {DriverId} replaced at Master-data revision {Revision}")]
    private static partial void LogReplaced(ILogger logger, string driverId, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Driver {DriverId} revision {Revision} is not higher than the copy's {Current}; nothing changed")]
    private static partial void LogStale(ILogger logger, string driverId, int revision, int current);
}
