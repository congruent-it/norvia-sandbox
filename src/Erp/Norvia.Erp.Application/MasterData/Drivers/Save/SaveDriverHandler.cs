using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.MasterData.Drivers;
using Norvia.Erp.Domain.Drivers;

namespace Norvia.Erp.Application.MasterData.Drivers.Save;

/// <summary>
/// Creates or edits a Driver. The change and its <c>Driver</c> event are saved by one save, so a rolled-back edit publishes nothing.
/// Making a Driver inactive stores <c>Active = false</c> and nothing else; the TMS decides what that means for its days and Trips.
/// </summary>
public sealed partial class SaveDriverHandler(
    IRepository<Driver> drivers,
    IOutbox outbox,
    TimeProvider time,
    ILogger<SaveDriverHandler> logger) : ICommandHandler<SaveDriverCommand, Result<SaveDriverResult>>
{
    public const int MaxNameLength = 200;
    public const int MaxIdLength = 50;

    public async ValueTask<Result<SaveDriverResult>> Handle(SaveDriverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Name))
        {
            return Refuse("The Driver needs an ID and a name.", "MasterDataFieldRequired");
        }

        if (command.Id.Length > MaxIdLength || command.Name.Length > MaxNameLength)
        {
            return Refuse($"The Driver's ID is at most {MaxIdLength} characters and its name {MaxNameLength}.", "MasterDataFieldTooLong");
        }

        var licences = new List<LicenceCategory>();
        foreach (var name in command.Licences ?? [])
        {
            if (!Enum.TryParse<LicenceCategory>(name, ignoreCase: false, out var licence) || !Enum.IsDefined(licence))
            {
                return Refuse("A Driver's licence is B, C or CE.", "MasterDataFieldInvalid");
            }

            licences.Add(licence);
        }

        var id = new DriverId(command.Id);
        var existing = await drivers.FirstOrDefaultAsync(new DriverByIdSpec(id), cancellationToken);
        MasterDataSaveOutcome outcome;
        Driver driver;
        if (existing is null)
        {
            driver = Driver.CreateByFinance(id, command.Name, licences, command.Active);
            await drivers.AddAsync(driver, cancellationToken);
            outcome = MasterDataSaveOutcome.Created;
        }
        else
        {
            driver = existing;
            if (!driver.Edit(command.Name, licences, command.Active))
            {
                LogUnchanged(logger, command.Id);
                return new SaveDriverResult(ViewOf(driver), MasterDataSaveOutcome.Unchanged);
            }

            await drivers.UpdateAsync(driver, cancellationToken);
            outcome = MasterDataSaveOutcome.Updated;
        }

        var view = ViewOf(driver);
        var correlationId = Guid.NewGuid().ToString();
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = correlationId,
            [TrackingKeys.Flow] = Flows.MasterData,
            [TrackingKeys.PartyId] = command.Id,
        });
        outbox.Add(new DriverV1(Guid.CreateVersion7(), time.GetUtcNow(), correlationId, view));
        LogSaved(logger, command.Id, outcome, view.Revision);
        return new SaveDriverResult(view, outcome);
    }

    internal static DriverView ViewOf(Driver driver) =>
        new(driver.Id.Value, driver.Name, [.. driver.Licences.Select(licence => licence.ToString())], driver.Active, driver.Revision);

    private static Result<SaveDriverResult> Refuse(string message, string code) =>
        Result<SaveDriverResult>.Invalid(new ValidationError("Driver", message, code, ValidationSeverity.Error));

    [LoggerMessage(Level = LogLevel.Information, Message = "Driver {DriverId} {Outcome} at Master-data revision {Revision}")]
    private static partial void LogSaved(ILogger logger, string driverId, MasterDataSaveOutcome outcome, int revision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Driver {DriverId} already has this content; nothing changed or published")]
    private static partial void LogUnchanged(ILogger logger, string driverId);
}
