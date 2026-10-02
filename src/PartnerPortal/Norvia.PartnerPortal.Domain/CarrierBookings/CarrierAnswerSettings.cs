using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// The answer policy the presenter chose for a Carrier Persona on its page. A Persona that was never changed has none
/// here and follows the seed file's default.
/// </summary>
public sealed class CarrierAnswerSettings : EntityBase<CarrierAnswerSettingsId>, IAggregateRoot
{
    // For EF Core, which sets the policy from its columns.
    private CarrierAnswerSettings()
    {
        PersonaId = null!;
        Policy = null!;
    }

    private CarrierAnswerSettings(CarrierAnswerSettingsId id, PersonaId personaId, CarrierAnswerPolicy policy)
    {
        Id = id;
        PersonaId = personaId;
        Policy = policy;
    }

    public PersonaId PersonaId { get; private set; }

    public CarrierAnswerPolicy Policy { get; private set; }

    public static CarrierAnswerSettings Choose(PersonaId personaId, CarrierAnswerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentNullException.ThrowIfNull(policy);
        return new CarrierAnswerSettings(CarrierAnswerSettingsId.New(), personaId, policy);
    }

    public void Change(CarrierAnswerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
    }
}
