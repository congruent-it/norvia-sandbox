using System.ComponentModel.DataAnnotations;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// What the form checks before it sends a booking: only what the Norvia Partner API's format needs to carry it
/// (contracts/partner-api/booking.v1.schema.json). Which fields a booking needs, and whether what they hold makes a
/// transport Norvia can carry, is for Norvia to judge; its answer names every reason at once.
/// </summary>
public static class BookingFormRules
{
    public const int MaxShipperReferenceLength = 100;

    private static readonly EmailAddressAttribute EmailAddress = new();

    public static IReadOnlyList<FormProblem> Check(BookingForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var problems = new List<FormProblem>();

        if (form.ShipperReference?.Trim().Length > MaxShipperReferenceLength)
        {
            problems.Add(new(form, nameof(BookingForm.ShipperReference), $"The Shipper reference can be at most {MaxShipperReferenceLength} characters."));
        }

        if (!string.IsNullOrWhiteSpace(form.Consignee.Email) && !EmailAddress.IsValid(form.Consignee.Email.Trim()))
        {
            problems.Add(new(form.Consignee, nameof(ConsigneeForm.Email), "Enter an email address, such as name@example.com."));
        }

        foreach (var line in form.GoodsLines)
        {
            problems.AddRange(Check(line));
        }

        return problems;
    }

    // The format writes a weight and dimensions without a sign, and dimensions as all three or none.
    private static IEnumerable<FormProblem> Check(GoodsLineForm line)
    {
        if (line.WeightKg < 0)
        {
            yield return new(line, nameof(GoodsLineForm.WeightKg), "The weight can't be negative.");
        }

        decimal?[] dimensions = [line.LengthCm, line.WidthCm, line.HeightCm];
        if (dimensions.Any(dimension => dimension is null) && dimensions.Any(dimension => dimension is not null))
        {
            yield return new(line, nameof(GoodsLineForm.LengthCm), "Give the length, width and height, or none of them.");
            yield break;
        }

        foreach (var (field, dimension) in new[] { (nameof(GoodsLineForm.LengthCm), line.LengthCm), (nameof(GoodsLineForm.WidthCm), line.WidthCm), (nameof(GoodsLineForm.HeightCm), line.HeightCm) })
        {
            if (dimension <= 0)
            {
                yield return new(line, field, "Each dimension must be more than 0 cm.");
            }
        }
    }
}
