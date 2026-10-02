using System.Text;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The text of a Service Bus message as the workflow passes it to the local functions. The Logic Apps trigger gives a
/// message's content base64-encoded, and the Functions runtime would read the fields of JSON text as parameters too,
/// so the workflow passes it as it got it (ADR 0019). JSON text passes through, so the same code runs on a message
/// read as text.
/// </summary>
public static class MessageContent
{
    public static string Text(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return content;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(content));
        }
        catch (FormatException)
        {
            return content;
        }
    }
}
