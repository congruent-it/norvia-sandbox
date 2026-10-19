using System.ComponentModel.DataAnnotations;
using Norvia.Erp.Application.CarrierInvoices;

namespace Norvia.Erp.Infrastructure.CarrierInvoices;

/// <summary>
/// The Matching tolerance, an ERP setting (<c>CarrierInvoices:MatchingTolerance</c>): in EUR, €0 by default, so a Carrier invoice line's
/// amount must equal its Carrier cost to the cent. One fixed amount for every Carrier; a percentage or a tolerance per Carrier is deferred.
/// </summary>
internal sealed class MatchingOptions : IMatchingTolerance
{
    public const string SectionName = "CarrierInvoices";

    [Range(typeof(decimal), "0", "1000000")]
    public decimal MatchingTolerance { get; set; }

    decimal IMatchingTolerance.Amount => MatchingTolerance;
}
