using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Application.Shippers.Replace;

public sealed partial class ReplaceShipperHandler(IShipperDirectory directory, IShipperReplicas replicas, ILogger<ReplaceShipperHandler> logger)
    : ICommandHandler<ReplaceShipperCommand, Result<ReplaceShipperResult>>
{
    public async ValueTask<Result<ReplaceShipperResult>> Handle(ReplaceShipperCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name) || command.Revision < 1)
        {
            return Result<ReplaceShipperResult>.Invalid(new ValidationError(
                "Shipper", "A Shipper needs an ID, a name and a Master-data revision of at least 1.", "MasterDataUnreadable", ValidationSeverity.Error));
        }

        var id = new ShipperId(command.Id);
        using var trail = logger.Track(new() { [TrackingKeys.PartyId] = command.Id });
        var current = directory.Find(id)?.Revision ?? 0;
        if (command.Revision <= current)
        {
            using var stale = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStale(logger, command.Id, command.Revision, current);
            return new ReplaceShipperResult(Applied: false, current);
        }

        await replicas.ReplaceAsync(new Shipper(id, command.Name, command.Active, command.Revision), cancellationToken);
        using var replaced = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        LogReplaced(logger, command.Id, command.Revision);
        return new ReplaceShipperResult(Applied: true, command.Revision);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper {ShipperId} replaced at Master-data revision {Revision}")]
    private static partial void LogReplaced(ILogger logger, string shipperId, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper {ShipperId} revision {Revision} is not higher than the copy's {Current}; nothing changed")]
    private static partial void LogStale(ILogger logger, string shipperId, int revision, int current);
}
