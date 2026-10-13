namespace Norvia.DriverApp.Domain.Reports;

/// <summary>
/// What the Proof of delivery form asks of the Driver (P12): who received the goods, clean or with reservations (a remark with reservations), a signature
/// drawn on screen as a PNG, and 0 to 4 photos, JPEG or PNG. That gives 1 to 5 files within P6's rules. The TMS decides all the same, and judges each file
/// by its content; the Driver app can only go by what the device said.
/// </summary>
public static class ProofOfDeliveryRules
{
    public const int MaximumPhotos = 4;

    /// <summary>P6's limit for one file.</summary>
    public const long MaximumFileBytes = MaximumFileMegabytes * 1024 * 1024;

    /// <summary>The longest name the TMS keeps of the person who signed.</summary>
    public const int MaximumReceivedByLength = 200;

    public const string PngContentType = "image/png";

    public const string JpegContentType = "image/jpeg";

    public const string SignatureName = "signature.png";

    private const int MaximumFileMegabytes = 10;

    /// <summary>What the Driver says of the reservations: only with reservations, and trimmed. A remark on a clean delivery is not kept.</summary>
    public static string? ReservationsRemark(DeliveryCondition? condition, string? remark) =>
        condition == DeliveryCondition.Reservations ? remark?.Trim() : null;

    /// <summary>The name a photo gets in the Proof of delivery, by its place in the form and what it is: the device's own file name is never used.</summary>
    public static string PhotoName(int number, string contentType) => $"photo-{number}{(contentType == PngContentType ? ".png" : ".jpg")}";

    public static IReadOnlyList<StopReportProblem> Check(ProofOfDeliveryForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var problems = new List<StopReportProblem>();
        if (string.IsNullOrWhiteSpace(form.ReceivedBy))
        {
            problems.Add(new("receivedBy", "Say who received the goods."));
        }
        else if (form.ReceivedBy.Trim().Length > MaximumReceivedByLength)
        {
            problems.Add(new("receivedBy", $"A name is at most {MaximumReceivedByLength} characters."));
        }

        if (form.Condition is null)
        {
            problems.Add(new("condition", "Say whether the goods were received clean or with reservations."));
        }
        else if (form.Condition == DeliveryCondition.Reservations && string.IsNullOrWhiteSpace(form.Remark))
        {
            problems.Add(new("remark", "A remark is required with reservations."));
        }

        if (form.Remark?.Trim().Length > StopReportRules.MaximumRemarkLength)
        {
            problems.Add(new("remark", $"A remark is at most {StopReportRules.MaximumRemarkLength} characters."));
        }

        if (form.Signature is null || form.Signature.SizeBytes == 0)
        {
            problems.Add(new("signature", "A signature is required."));
        }
        else if (form.Signature.ContentType != PngContentType || form.Signature.SizeBytes > MaximumFileBytes)
        {
            problems.Add(new("signature", $"The signature must be a PNG of at most {MaximumFileMegabytes} MB."));
        }

        if (form.Photos.Count > MaximumPhotos)
        {
            problems.Add(new("photos", $"At most {MaximumPhotos} photos."));
        }
        else if (form.Photos.Any(photo => photo.ContentType is not (JpegContentType or PngContentType) || photo.SizeBytes == 0 || photo.SizeBytes > MaximumFileBytes))
        {
            problems.Add(new("photos", $"A photo must be a JPEG or a PNG of at most {MaximumFileMegabytes} MB."));
        }

        return problems;
    }
}
