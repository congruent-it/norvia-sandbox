using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Application.Carriers.Replace;

public sealed partial class ReplaceCarrierHandler(ICarrierDirectory directory, ICarrierReplicas replicas, ILogger<ReplaceCarrierHandler> logger)
    : ICommandHandler<ReplaceCarrierCommand, Result<ReplaceCarrierResult>>
{
    public async ValueTask<Result<ReplaceCarrierResult>> Handle(ReplaceCarrierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name) || command.Revision < 1)
        {
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = "MasterDataUnreadable" });
            return Result<ReplaceCarrierResult>.Invalid(new ValidationError(
                "Carrier", "A Carrier needs an ID, a name and a Master-data revision of at least 1.", "MasterDataUnreadable", ValidationSeverity.Error));
        }

        var id = new CarrierId(command.Id);
        using var trail = logger.Track(new() { [TrackingKeys.PartyId] = command.Id });
        var current = directory.Find(id)?.Revision ?? 0;
        if (command.Revision <= current)
        {
            using var stale = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStale(logger, command.Id, command.Revision, current);
            return new ReplaceCarrierResult(Applied: false, current);
        }

        await replicas.ReplaceAsync(new Carrier(id, command.Name, command.Active, command.Revision), cancellationToken);
        using var replaced = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        LogReplaced(logger, command.Id, command.Revision);
        return new ReplaceCarrierResult(Applied: true, command.Revision);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier {CarrierId} replaced at Master-data revision {Revision}")]
    private static partial void LogReplaced(ILogger logger, string carrierId, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier {CarrierId} revision {Revision} is not higher than the copy's {Current}; nothing changed")]
    private static partial void LogStale(ILogger logger, string carrierId, int revision, int current);
}
