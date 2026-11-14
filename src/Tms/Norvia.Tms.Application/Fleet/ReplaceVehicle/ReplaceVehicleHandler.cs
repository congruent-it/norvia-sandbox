using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.ReplaceVehicle;

public sealed partial class ReplaceVehicleHandler(IVehicleDirectory directory, IVehicleReplicas replicas, ILogger<ReplaceVehicleHandler> logger)
    : ICommandHandler<ReplaceVehicleCommand, Result<ReplaceVehicleResult>>
{
    public async ValueTask<Result<ReplaceVehicleResult>> Handle(ReplaceVehicleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Registration) || command.Revision < 1
            || !Enum.TryParse<VehicleType>(command.Type, ignoreCase: false, out var type) || !Enum.IsDefined(type)
            || command.PayloadKg < 1 || command.PalletPlaces < 0)
        {
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = "MasterDataUnreadable" });
            return Result<ReplaceVehicleResult>.Invalid(new ValidationError(
                "Vehicle",
                "A Vehicle needs an ID, a registration, a known type, a payload, pallet places and a Master-data revision of at least 1.",
                "MasterDataUnreadable",
                ValidationSeverity.Error));
        }

        var id = new VehicleId(command.Id);
        using var trail = logger.Track(new() { [TrackingKeys.PartyId] = command.Id });
        var current = directory.Find(id)?.Revision ?? 0;
        if (command.Revision <= current)
        {
            using var stale = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStale(logger, command.Id, command.Revision, current);
            return new ReplaceVehicleResult(Applied: false, current);
        }

        var applied = await replicas.ReplaceAsync(new Vehicle(id, command.Registration, type, command.PayloadKg, command.PalletPlaces, command.Active, command.Revision), cancellationToken);
        if (!applied)
        {
            // A later revision got in between the check and the write.
            using var lost = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            var now = directory.Find(id)?.Revision ?? command.Revision;
            LogStale(logger, command.Id, command.Revision, now);
            return new ReplaceVehicleResult(Applied: false, now);
        }

        using var replaced = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        LogReplaced(logger, command.Id, command.Revision);
        return new ReplaceVehicleResult(Applied: true, command.Revision);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Vehicle {VehicleId} replaced at Master-data revision {Revision}")]
    private static partial void LogReplaced(ILogger logger, string vehicleId, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vehicle {VehicleId} revision {Revision} is not higher than the copy's {Current}; nothing changed")]
    private static partial void LogStale(ILogger logger, string vehicleId, int revision, int current);
}
