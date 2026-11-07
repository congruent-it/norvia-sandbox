using System.Text.RegularExpressions;

namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed question Finance puts to the Carrier invoice matching agent: one per Explain button. Nothing free-form reaches the model
/// (ADR 0040). The scripted model reads the same text back to tell it from another question.
/// </summary>
public static partial class FinanceQuestions
{
    private const string Prefix = "Explain why the line \"";
    private const string InvoiceIntro = "\" of Carrier invoice \"";
    private const string CarrierIntro = "\" of Carrier \"";
    private const string Suffix = "\" did not match, and whether it may still resolve.";

    public static string ExplainInvoiceLine(string carrierId, string invoiceNumber, string carrierBookingReference)
    {
        ArgumentNullException.ThrowIfNull(carrierId);
        ArgumentNullException.ThrowIfNull(invoiceNumber);
        ArgumentNullException.ThrowIfNull(carrierBookingReference);
        return Prefix + carrierBookingReference + InvoiceIntro + invoiceNumber + CarrierIntro + carrierId + Suffix;
    }

    /// <summary>The Carrier, invoice number and Carrier booking reference of an Explain question, or null when the text is another question.</summary>
    public static (string CarrierId, string InvoiceNumber, string CarrierBookingReference)? ParseExplainInvoiceLine(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var match = Explain().Match(question);
        return match.Success
            ? (match.Groups["carrier"].Value, match.Groups["invoice"].Value, match.Groups["reference"].Value)
            : null;
    }

    /// <summary>
    /// Whether a value may be put in the question and passed to a tool: a plain identifier of letters, digits, '-', '_', '.' and ':'. The
    /// Carrier typed the invoice number and the reference, so anything else could carry an instruction.
    /// </summary>
    public static bool IsPlainIdentifier(string? value) =>
        value is { Length: > 0 and <= 200 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    [GeneratedRegex("^Explain why the line \"(?<reference>[^\"]+)\" of Carrier invoice \"(?<invoice>[^\"]+)\" of Carrier \"(?<carrier>[^\"]+)\" did not match, and whether it may still resolve\\.$")]
    private static partial Regex Explain();
}
