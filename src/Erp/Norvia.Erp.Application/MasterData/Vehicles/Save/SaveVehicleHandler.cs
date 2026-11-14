using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Vehicles;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Application.MasterData.Vehicles.Save;

/// <summary>
/// Creates or edits a Vehicle. The change and its <c>Vehicle</c> event are saved by one save, so a rolled-back edit publishes nothing.
/// Making a Vehicle inactive stores <c>Active = false</c> and nothing else; the TMS decides what that means for its days and Trips.
/// </summary>
public sealed partial class SaveVehicleHandler(
    IRepository<Vehicle> vehicles,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SaveVehicleHandler> logger) : ICommandHandler<SaveVehicleCommand, Result<SaveVehicleResult>>
{
    public const int MaxRegistrationLength = 50;
    public const int MaxIdLength = 50;

    public async ValueTask<Result<SaveVehicleResult>> Handle(SaveVehicleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (MasterDataSaveKey.IsTooLong(command.IdempotencyKey))
        {
            return Refuse(MasterDataSaveKey.TooLongMessage, MasterDataSaveKey.TooLongCode);
        }

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Registration))
        {
            return Refuse("The Vehicle needs an ID and a registration.", "MasterDataFieldRequired");
        }

        if (command.Id.Length > MaxIdLength || command.Registration.Length > MaxRegistrationLength)
        {
            return Refuse($"The Vehicle's ID and registration are at most {MaxIdLength} characters.", "MasterDataFieldTooLong");
        }

        if (!Enum.TryParse<VehicleType>(command.Type, ignoreCase: false, out var type) || !Enum.IsDefined(type))
        {
            return Refuse("The Vehicle's type is Van, RigidTruck or TractorWithTrailer.", "MasterDataFieldInvalid");
        }

        if (command.PayloadKg < 1 || command.PalletPlaces < 0)
        {
            return Refuse("The Vehicle's payload is at least 1 kg and its pallet places are not negative.", "MasterDataFieldInvalid");
        }

        var id = new VehicleId(command.Id);
        var registration = command.Registration.Trim();
        if (await vehicles.FirstOrDefaultAsync(new VehicleByRegistrationSpec(registration), cancellationToken) is { } holder && holder.Id != id)
        {
            return Refuse($"Vehicle {holder.Id.Value} already has the registration {registration}.", "VehicleRegistrationTaken");
        }

        var existing = await vehicles.FirstOrDefaultAsync(new VehicleByIdSpec(id), cancellationToken);
        MasterDataSaveOutcome outcome;
        Vehicle vehicle;
        if (existing is null)
        {
            vehicle = Vehicle.CreateByFinance(id, registration, type, command.PayloadKg, command.PalletPlaces, command.Active, command.IdempotencyKey);
            await vehicles.AddAsync(vehicle, cancellationToken);
            outcome = MasterDataSaveOutcome.Created;
        }
        else
        {
            vehicle = existing;
            if (vehicle.IsRepeat(command.IdempotencyKey))
            {
                LogRepeat(logger, command.Id);
                return new SaveVehicleResult(ViewOf(vehicle), MasterDataSaveOutcome.Repeat);
            }

            if (!vehicle.Edit(registration, type, command.PayloadKg, command.PalletPlaces, command.Active, command.IdempotencyKey))
            {
                LogUnchanged(logger, command.Id);
                return new SaveVehicleResult(ViewOf(vehicle), MasterDataSaveOutcome.Unchanged);
            }

            await vehicles.UpdateAsync(vehicle, cancellationToken);
            outcome = MasterDataSaveOutcome.Updated;
        }

        var view = ViewOf(vehicle);
        var correlationId = Guid.NewGuid().ToString();
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.MasterData,
            [TrackingKeys.PartyId] = command.Id,
        });
        outbox.Add(new VehicleChangedV1(Guid.CreateVersion7(), time.GetUtcNow(), correlationId, view));
        LogSaved(logger, command.Id, outcome, view.Revision);
        return new SaveVehicleResult(view, outcome);
    }

    internal static VehicleView ViewOf(Vehicle vehicle) =>
        new(vehicle.Id.Value, vehicle.Registration, vehicle.Type.ToString(), vehicle.PayloadKg, vehicle.PalletPlaces, vehicle.Active, vehicle.Revision);

    private static Result<SaveVehicleResult> Refuse(string message, string code) =>
        Result<SaveVehicleResult>.Invalid(new ValidationError("Vehicle", message, code, ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Vehicle {VehicleId} {Outcome} at Master-data revision {Revision}")]
    private static partial void LogSaved(ILogger logger, string vehicleId, MasterDataSaveOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vehicle {VehicleId}: this save was applied before under its Idempotency-Key; nothing changed or published")]
    private static partial void LogRepeat(ILogger logger, string vehicleId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vehicle {VehicleId} already has this content; nothing changed or published")]
    private static partial void LogUnchanged(ILogger logger, string vehicleId);
}
