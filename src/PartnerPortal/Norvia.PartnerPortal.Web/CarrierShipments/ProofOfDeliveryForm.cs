using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>The Send Proof of delivery form's model: who received the goods, in what condition, and the remark that reservations need.</summary>
public sealed class ProofOfDeliveryForm
{
    public const string ReceivedByRequired = "Enter who received the goods.";
    public const string RemarkRequired = "Say what the reservations are.";
    public const int RemarkMaxLength = 500;

    [Required(ErrorMessage = ReceivedByRequired)]
    [MaxLength(200, ErrorMessage = "Keep the name to 200 characters.")]
    public string? ReceivedBy { get; set; }

    public ProofOfDeliveryCondition Condition { get; set; } = ProofOfDeliveryCondition.Clean;

    /// <summary>The condition as the select binds it: by the enum's name.</summary>
    public string ConditionName
    {
        get => Condition.ToString();
        set => Condition = Enum.Parse<ProofOfDeliveryCondition>(value);
    }

    [RemarkWithReservations(ErrorMessage = RemarkRequired)]
    [MaxLength(RemarkMaxLength, ErrorMessage = "Keep the remark to 500 characters.")]
    public string? Remark { get; set; }

    /// <summary>Whether the sample CMR bundled with the portal goes with the Proof of delivery.</summary>
    public bool UseSampleCmr { get; set; } = true;

    /// <summary>The remark is required when the form's condition is <see cref="ProofOfDeliveryCondition.Reservations"/>.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class RemarkWithReservationsAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            ArgumentNullException.ThrowIfNull(validationContext);
            return validationContext.ObjectInstance is ProofOfDeliveryForm { Condition: ProofOfDeliveryCondition.Reservations } && string.IsNullOrWhiteSpace(value as string)
                ? new ValidationResult(ErrorMessage, [validationContext.MemberName!])
                : ValidationResult.Success;
        }
    }
}
