using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;

public sealed class ChangeCarrierTrackingSettingsHandler(IRepository<CarrierTrackingSettings> repository, IPersonaDirectory personas)
    : ICommandHandler<ChangeCarrierTrackingSettingsCommand, Result<CarrierTrackingSettingsView>>
{
    public async ValueTask<Result<CarrierTrackingSettingsView>> Handle(ChangeCarrierTrackingSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(command.PersonaId) is not { Carrier: not null } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{command.PersonaId}'.");
        }

        if (command.DelaySeconds < 0 || command.DelaySeconds > CarrierTrackingPolicy.LongestDelay.TotalSeconds)
        {
            return Result.Invalid(new ValidationError(nameof(command.DelaySeconds), $"The delay is between 0 and {CarrierTrackingPolicy.LongestDelay.TotalSeconds:0} seconds."));
        }

        if (!Enum.IsDefined(command.Mode))
        {
            return Result.Invalid(new ValidationError(nameof(command.Mode), "Choose one of the tracking modes."));
        }

        var policy = new CarrierTrackingPolicy(command.Mode, TimeSpan.FromSeconds(command.DelaySeconds));
        var chosen = await repository.FirstOrDefaultAsync(new CarrierTrackingSettingsOfPersonaSpec(carrier.Id), cancellationToken);
        if (chosen is null)
        {
            await repository.AddAsync(CarrierTrackingSettings.Choose(carrier.Id, policy), cancellationToken);
        }
        else
        {
            chosen.Change(policy);
            await repository.UpdateAsync(chosen, cancellationToken);
        }

        return CarrierTrackingSettingsView.From(policy);
    }
}
