using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>The confirm form's model: the Carrier's own booking reference, and when it plans to collect, if it says.</summary>
public sealed class ConfirmForm
{
    public const string ReferenceRequired = "Enter your own booking reference.";

    [Required(ErrorMessage = ReferenceRequired)]
    public string? BookingReference { get; set; }

    /// <summary>The time as typed: Rotterdam's, as the portal shows every time.</summary>
    public DateTime? PlannedCollection { get; set; }

    public CarrierAnswer.Confirmation ToAnswer() =>
        new(BookingReference!, PlannedCollection is { } planned ? PortalTime.ToOffset(planned) : null);
}
