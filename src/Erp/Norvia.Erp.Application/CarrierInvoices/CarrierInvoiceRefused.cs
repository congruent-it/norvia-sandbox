using Ardalis.Result;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices;

/// <summary>
/// A Carrier invoice the ERP refused, as a result: Invalid, with one error carrying the refusal's message and its stable code, so the API
/// answers with it and the trace records it as the <c>failureCode</c>.
/// </summary>
public static class CarrierInvoiceRefused
{
    public static Result<TResult> Because<TResult>(CarrierInvoiceRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("CarrierInvoice", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }
}
