using System.Globalization;
using Norvia.DriverApp.Application.Reports.Deliver;

namespace Norvia.DriverApp.Web.Reports;

/// <summary>
/// What the Proof of delivery form posts, read into what the Deliver command takes. The signature is drawn on a canvas and posted as a PNG data URL; the photos are
/// files. The form carries no Driver ID: it comes from the token (ADR 0005).
/// </summary>
internal sealed record ProofOfDeliveryForm(string? ReceivedBy, DeliveryConditionDto? Condition, string? Remark, ProofFile? Signature, IReadOnlyList<ProofFile> Photos)
{
    /// <summary>The signature and the most photos at the most each can be, with room for the fields.</summary>
    public const long MaximumRequestBytes = (DeliverCommand.MaximumPhotos + 1) * DeliverCommand.MaximumFileBytes + (1024 * 1024);

    private const string PngDataUrlPrefix = "data:image/png;base64,";

    /// <returns>The form, or null when a file is over the limit, which is not read.</returns>
    public static async Task<ProofOfDeliveryForm?> ReadAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var photos = new List<ProofFile>();
        foreach (var file in form.Files.GetFiles("photos").Where(file => file.Length > 0))
        {
            if (file.Length > DeliverCommand.MaximumFileBytes)
            {
                return null;
            }

            await using var stream = file.OpenReadStream();
            using var content = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(content, cancellationToken);
            photos.Add(new ProofFile(file.ContentType, content.ToArray()));
        }

        return new ProofOfDeliveryForm(
            form["receivedBy"].ToString(),
            form["condition"].ToString().ToLower(CultureInfo.InvariantCulture) switch
            {
                "clean" => DeliveryConditionDto.Clean,
                "reservations" => DeliveryConditionDto.Reservations,
                _ => null,
            },
            form["remark"].ToString(),
            SignatureOf(form["signature"].ToString()),
            photos);
    }

    /// <summary>The PNG a canvas posts as a data URL, or null when the Driver drew nothing or the field isn't one.</summary>
    internal static ProofFile? SignatureOf(string dataUrl)
    {
        if (!dataUrl.StartsWith(PngDataUrlPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var bytes = new byte[(dataUrl.Length - PngDataUrlPrefix.Length) * 3 / 4 + 3];
        return Convert.TryFromBase64String(dataUrl[PngDataUrlPrefix.Length..], bytes, out var written) && written > 0
            ? new ProofFile(DriverApp.Domain.Reports.ProofOfDeliveryRules.PngContentType, bytes.AsMemory(0, written).ToArray())
            : null;
    }
}
