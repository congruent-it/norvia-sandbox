using System.ComponentModel.DataAnnotations;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>The reject form's model: a reason code, and its text, which the reason Other needs.</summary>
public sealed class RejectForm
{
    public const string TextRequired = "Say why, when the reason is Other.";
    public const int TextMaxLength = 500;

    public CarrierRejectionReason Reason { get; set; } = CarrierRejectionReason.NoCapacity;

    /// <summary>The reason as the select binds it: by the enum's name.</summary>
    public string ReasonName
    {
        get => Reason.ToString();
        set => Reason = Enum.Parse<CarrierRejectionReason>(value);
    }

    [TextForOther(ErrorMessage = TextRequired)]
    [MaxLength(TextMaxLength, ErrorMessage = "Keep the reason text to 500 characters.")]
    public string? Text { get; set; }

    public CarrierAnswer.Rejection ToAnswer() => new(Reason, Text);

    /// <summary>The text is required when the form's reason is <see cref="CarrierRejectionReason.Other"/>.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class TextForOtherAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            ArgumentNullException.ThrowIfNull(validationContext);
            return validationContext.ObjectInstance is RejectForm { Reason: CarrierRejectionReason.Other } && string.IsNullOrWhiteSpace(value as string)
                ? new ValidationResult(ErrorMessage, [validationContext.MemberName!])
                : ValidationResult.Success;
        }
    }
}
