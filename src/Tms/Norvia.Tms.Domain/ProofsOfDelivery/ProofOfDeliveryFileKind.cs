namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>What a Proof of delivery file really is, judged by its content and not by its name or declared type (P6).</summary>
public static class ProofOfDeliveryFileKind
{
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The content type of a PDF, JPEG or PNG, read from the start of its content, or null for anything else.</summary>
    public static string? Detect(ReadOnlySpan<byte> content) =>
        content.StartsWith(Pdf) ? "application/pdf"
        : content.StartsWith(Jpeg) ? "image/jpeg"
        : content.StartsWith(Png) ? "image/png"
        : null;
}
