using System.Globalization;

namespace Norvia.Erp.Web.BillableLines;

/// <summary>How the ERP UI shows an amount in euros without VAT.</summary>
internal static class Money
{
    public static string Format(decimal amount) => string.Create(CultureInfo.InvariantCulture, $"€ {amount:N2}");
}
