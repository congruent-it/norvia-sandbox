using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;

public sealed class ChangeCarrierAnswerSettingsHandler(IRepository<CarrierAnswerSettings> repository, IPersonaDirectory personas)
    : ICommandHandler<ChangeCarrierAnswerSettingsCommand, Result<CarrierAnswerSettingsView>>
{
    public async ValueTask<Result<CarrierAnswerSettingsView>> Handle(ChangeCarrierAnswerSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(new PersonaId(command.PersonaId)) is not { Carrier: not null } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{command.PersonaId}'.");
        }

        if (command.DelaySeconds < 0 || command.DelaySeconds > CarrierAnswerPolicy.LongestDelay.TotalSeconds)
        {
            return Result.Invalid(new ValidationError(nameof(command.DelaySeconds), $"The delay is between 0 and {CarrierAnswerPolicy.LongestDelay.TotalSeconds:0} seconds."));
        }

        if (!Enum.IsDefined(command.Mode) || !Enum.IsDefined(command.RejectionReason))
        {
            return Result.Invalid(new ValidationError(nameof(command.Mode), "Choose one of the answer modes and rejection reasons."));
        }

        var policy = new CarrierAnswerPolicy(command.Mode, command.RejectionReason, TimeSpan.FromSeconds(command.DelaySeconds));
        var chosen = await repository.FirstOrDefaultAsync(new CarrierAnswerSettingsOfPersonaSpec(carrier.Id), cancellationToken);
        if (chosen is null)
        {
            await repository.AddAsync(CarrierAnswerSettings.Choose(carrier.Id, policy), cancellationToken);
        }
        else
        {
            chosen.Change(policy);
            await repository.UpdateAsync(chosen, cancellationToken);
        }

        return CarrierAnswerSettingsView.From(policy);
    }
}
