using System.Globalization;
using System.Text;
using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// A Carrier's Proof of delivery as a one-page text PDF that reads like a delivery receipt. It is written by hand (a few hundred bytes,
/// no PDF dependency) and holds nothing but what the Simulator already sent for that Shipment. The same Proof gives the same bytes.
/// </summary>
internal static class ProofOfDeliveryDocument
{
    public static byte[] Create(CarrierProofRequest proof, string deliveryTime)
    {
        var lines = new List<(int Size, string Text)>
        {
            (18, "Proof of delivery"),
            (12, proof.CarrierName),
            (12, $"Shipment: {proof.ShipmentNumber}"),
            (12, $"Transport Order: {proof.TransportOrderNumber}"),
            (12, $"Carrier booking reference: {proof.BookingReference}"),
            (12, $"Delivered: {deliveryTime} at {proof.Place.City}"),
            (12, $"Received by: {proof.ReceivedBy}"),
            (12, $"Condition: {(proof.Reservations ? "Reservations" : "Clean")}"),
        };
        if (!string.IsNullOrWhiteSpace(proof.Remark))
        {
            lines.Add((12, $"Remark: {proof.Remark.Trim()}"));
        }

        var content = new StringBuilder("BT\n/F1 12 Tf\n56 780 Td\n18 TL\n");
        foreach (var (size, text) in lines)
        {
            content.Append(CultureInfo.InvariantCulture, $"/F1 {size} Tf\n({Escape(text)}) Tj\nT*\n");
        }

        content.Append("ET");
        var stream = content.ToString();

        var objects = new[]
        {
            "<</Type /Catalog /Pages 2 0 R>>",
            "<</Type /Pages /Kids [3 0 R] /Count 1>>",
            "<</Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources <</Font <</F1 4 0 R>>>> /Contents 5 0 R>>",
            "<</Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding>>",
            $"<</Length {stream.Length}>>\nstream\n{stream}\nendstream",
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var number = 1; number <= objects.Length; number++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{objects[number - 1]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size {objects.Length + 1} /Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    // One byte per character, so a character outside Latin-1 becomes '?'; the string's own brackets and backslashes are escaped.
    private static string Escape(string text)
    {
        var escaped = new StringBuilder();
        foreach (var character in text.ReplaceLineEndings(" "))
        {
            if (character is '\\' or '(' or ')')
            {
                escaped.Append('\\');
            }

            escaped.Append(character > 'ÿ' ? '?' : character);
        }

        return escaped.ToString();
    }
}
