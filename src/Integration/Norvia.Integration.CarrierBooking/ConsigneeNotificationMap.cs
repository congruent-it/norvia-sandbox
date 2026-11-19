using System.Net.Mail;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The Consignee notification map (#983): reads a Canonical Model <c>ConsigneeNotificationRequested</c>, and makes the email that tells the
/// Consignee where to track the goods from it, the Consignee's address and the tracking code the TMS gave, and the Partner portal's address.
/// </summary>
/// <remarks>
/// The email carries the link and nothing else (ADR 0052): no Transport Order or Shipment number, no Shipper, no Carrier and no times, so a lost
/// email leaks no more than the link, and the tracking page shows the agreed window. The address and the code are secrets: nothing here
/// writes either to an error or a log.
/// </remarks>
public static partial class ConsigneeNotificationMap
{
    private const string Collected = "Collected";
    private const string DeliveryWindowAgreed = "DeliveryWindowAgreed";

    /// <summary>The Transport Order's tracking code as the TMS makes it: 16 characters of Crockford base32 (ADR 0052).</summary>
    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{16}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TrackingCodePattern();

    /// <param name="consigneeNotificationRequested">The <c>ConsigneeNotificationRequested</c> event as JSON text, as the Service Bus message carries it.</param>
    /// <returns>The Transport Order and the reason, or null when the text isn't a notification the Consignee can be told of.</returns>
    public static JsonObject? ToNotification(string consigneeNotificationRequested)
    {
        if (JsonFields.Parse(consigneeNotificationRequested) is not { } requested
            || JsonFields.Text(requested, "transportOrderNumber") is not { } transportOrderNumber
            || JsonFields.Text(requested, "reason") is not ({ } reason and (Collected or DeliveryWindowAgreed)))
        {
            return null;
        }

        return new JsonObject
        {
            ["transportOrderNumber"] = transportOrderNumber,
            ["reason"] = reason,
        };
    }

    /// <param name="consigneeNotificationRequested">The event, as <see cref="ToNotification"/> reads it.</param>
    /// <param name="consigneeNotificationContent">What the TMS gave for the Transport Order: <c>transportOrderNumber</c>, <c>email</c> and <c>trackingCode</c>.</param>
    /// <param name="trackingBaseUrl">The Partner portal's address, which the link <c>/track/{code}</c> is on.</param>
    /// <returns>The email as <c>to</c>, <c>subject</c> and <c>plainText</c>, or null when the event, the content or the address can't be used, so nothing is sent.</returns>
    public static JsonObject? ToEmail(string consigneeNotificationRequested, string consigneeNotificationContent, string trackingBaseUrl)
    {
        if (ToNotification(consigneeNotificationRequested) is not { } notification
            || JsonFields.Parse(consigneeNotificationContent) is not { } content
            || JsonFields.Text(content, "transportOrderNumber") != notification["transportOrderNumber"]!.GetValue<string>()
            || JsonFields.Text(content, "email")?.Trim() is not { } email || !IsAddress(email)
            || JsonFields.Text(content, "trackingCode") is not { } code || !TrackingCodePattern().IsMatch(code)
            || !Uri.TryCreate(trackingBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
        {
            return null;
        }

        var link = $"{baseUri.GetLeftPart(UriPartial.Authority)}{baseUri.AbsolutePath.TrimEnd('/')}/track/{code}";
        var collected = notification["reason"]!.GetValue<string>() == Collected;
        var what = collected
            ? "Your goods have been collected and are on their way to you."
            : "A delivery window has been agreed for your goods.";
        return new JsonObject
        {
            ["to"] = email,
            ["subject"] = collected ? "Your goods have been collected: track them here" : "A delivery window has been agreed for your goods",
            ["plainText"] = $"{what}\n\nSee where they are, and when we expect to deliver them:\n{link}\n\nThe link is for you. Anyone who has it can see the status, so please don't pass it on.\n\nNorvia",
        };
    }

    private static bool IsAddress(string text) =>
        MailAddress.TryCreate(text, out var address) && address.Address == text && address.DisplayName.Length == 0
        && address.Host.Contains('.', StringComparison.Ordinal) && !address.Host.StartsWith('.') && !address.Host.EndsWith('.');
}
