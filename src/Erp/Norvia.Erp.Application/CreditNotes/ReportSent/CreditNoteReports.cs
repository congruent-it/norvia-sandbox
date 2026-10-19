using Ardalis.Result;
using Norvia.Erp.Domain.CreditNotes;

namespace Norvia.Erp.Application.CreditNotes.ReportSent;

internal static class CreditNoteReports
{
    public static Result<T> NotFound<T>(string? creditNoteNumber) =>
        Result<T>.Invalid(
            new ValidationError(
                "CreditNote",
                $"The ERP has no Credit note {creditNoteNumber}.",
                nameof(CreditNoteRefusalCode.CreditNoteNotFound),
                ValidationSeverity.Error));
}
