using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.CarrierBookings;

/// <summary>
/// The tracking policy the presenter chose for a Carrier Persona on its page. A Persona that was never changed has none
/// here and follows the seed file's default.
/// </summary>
public sealed class CarrierTrackingSettings : EntityBase<CarrierTrackingSettingsId>, IAggregateRoot
{
    // For EF Core, which sets the policy from its columns.
    private CarrierTrackingSettings()
    {
        PersonaId = null!;
        Policy = null!;
    }

    private CarrierTrackingSettings(CarrierTrackingSettingsId id, PersonaId personaId, CarrierTrackingPolicy policy)
    {
        Id = id;
        PersonaId = personaId;
        Policy = policy;
    }

    public PersonaId PersonaId { get; private set; }

    public CarrierTrackingPolicy Policy { get; private set; }

    public static CarrierTrackingSettings Choose(PersonaId personaId, CarrierTrackingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(personaId);
        ArgumentNullException.ThrowIfNull(policy);
        return new CarrierTrackingSettings(CarrierTrackingSettingsId.New(), personaId, policy);
    }

    public void Change(CarrierTrackingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
    }
}
