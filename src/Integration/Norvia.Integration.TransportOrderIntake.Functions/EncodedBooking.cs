using System.Text;

namespace Norvia.Integration.TransportOrderIntake.Functions;

/// <summary>
/// The booking as the workflow passes it to the local functions: the JSON text the Shipper sent, base64-encoded. The
/// Functions runtime would otherwise read its fields as parameters too, and a <c>shipperId</c> field would choose the
/// Shipper (ADR 0005).
/// </summary>
internal static class EncodedBooking
{
    public static string Decode(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
}
