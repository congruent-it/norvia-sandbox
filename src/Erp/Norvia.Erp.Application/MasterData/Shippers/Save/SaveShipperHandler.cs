using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.MasterData.Shippers.Save;

/// <summary>
/// Creates or edits a Shipper. The change and its <c>Shipper</c> event are saved by one save, so a rolled-back edit publishes nothing.
/// </summary>
public sealed partial class SaveShipperHandler(
    IRepository<Shipper> shippers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SaveShipperHandler> logger) : ICommandHandler<SaveShipperCommand, Result<SaveShipperResult>>
{
    public const int MaxNameLength = 200;
    public const int MaxIdLength = 50;

    public async ValueTask<Result<SaveShipperResult>> Handle(SaveShipperCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name))
        {
            return Refuse("The Shipper needs an ID and a name.", "MasterDataFieldRequired");
        }

        if (command.Id.Length > MaxIdLength || command.Name.Length > MaxNameLength)
        {
            return Refuse($"The Shipper's ID is at most {MaxIdLength} characters and its name {MaxNameLength}.", "MasterDataFieldTooLong");
        }

        var id = new ShipperId(command.Id);
        var existing = await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(id), cancellationToken);
        MasterDataSaveOutcome outcome;
        Shipper shipper;
        if (existing is null)
        {
            shipper = Shipper.CreateByFinance(id, command.Name, command.Active);
            await shippers.AddAsync(shipper, cancellationToken);
            outcome = MasterDataSaveOutcome.Created;
        }
        else
        {
            shipper = existing;
            if (!shipper.Edit(command.Name, command.Active))
            {
                LogUnchanged(logger, command.Id);
                return new SaveShipperResult(ViewOf(shipper), MasterDataSaveOutcome.Unchanged);
            }

            await shippers.UpdateAsync(shipper, cancellationToken);
            outcome = MasterDataSaveOutcome.Updated;
        }

        var view = ViewOf(shipper);
        var correlationId = Guid.NewGuid().ToString();
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.MasterData,
            [TrackingKeys.PartyId] = command.Id,
        });
        outbox.Add(new ShipperV1(Guid.CreateVersion7(), time.GetUtcNow(), correlationId, view));
        LogSaved(logger, command.Id, outcome, view.Revision);
        return new SaveShipperResult(view, outcome);
    }

    private static ShipperView ViewOf(Shipper shipper) => new(shipper.Id.Value, shipper.Name, shipper.Active, shipper.Revision);

    private static Result<SaveShipperResult> Refuse(string message, string code) =>
        Result<SaveShipperResult>.Invalid(new ValidationError("Shipper", message, code, ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper {ShipperId} {Outcome} at Master-data revision {Revision}")]
    private static partial void LogSaved(ILogger logger, string shipperId, MasterDataSaveOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper {ShipperId} already has this content; nothing changed or published")]
    private static partial void LogUnchanged(ILogger logger, string shipperId);
}
