using System.Net.Mail;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The Consignee's optional email address (#983), where the TMS has the Consignee told how to track the goods. It is personal data,
/// like the Consignee's name and phone number, and is never logged.
/// </summary>
public static class ConsigneeEmail
{
    /// <summary>
    /// Whether the text is one plain address, such as <c>name@example.com</c>: no display name, no list, and a domain with a dot. Leading
    /// and trailing spaces are not held against it, because the TMS trims the address when it uses it.
    /// </summary>
    public static bool IsValid(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed || address.DisplayName.Length > 0)
        {
            return false;
        }

        var domain = address.Host;
        return domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
