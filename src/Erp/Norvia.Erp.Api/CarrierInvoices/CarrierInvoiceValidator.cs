using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.CarrierInvoices;

/// <summary>
/// Checks a Carrier invoice against the Canonical Model's own schema rules, at the boundary (coding guidelines §6), and maps it to what
/// the domain takes. The receiving rules (the Carrier exists, a line has a reference and an amount above zero, no reference twice, the
/// invoice number is the Carrier's own) are the ERP's, and answer with their stable codes.
/// </summary>
internal static class CarrierInvoiceValidator
{
    private const int MaxReferenceLength = 200;
    private const int MaxDescriptionLength = 500;

    public static (CarrierInvoiceData? Invoice, Dictionary<string, string[]> Errors) Validate(CarrierInvoiceRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors[string.Empty] = ["The body must be a Carrier invoice."];
            return (null, errors);
        }

        Require(errors, !string.IsNullOrWhiteSpace(request.CarrierId), nameof(request.CarrierId), "The carrierId is required.");
        Require(errors, !string.IsNullOrWhiteSpace(request.CorrelationId), nameof(request.CorrelationId), "The correlationId is required.");
        Require(
            errors,
            !string.IsNullOrWhiteSpace(request.InvoiceNumber) && request.InvoiceNumber.Length <= CarrierInvoice.MaxInvoiceNumberLength,
            nameof(request.InvoiceNumber),
            $"The invoiceNumber is required, of at most {CarrierInvoice.MaxInvoiceNumberLength} characters.");

        // The deserializer refuses a missing or null member, so the lines are there. An empty one, or one with a blank reference or an
        // amount of zero or less, is the ERP's to refuse with its own code.
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var field = $"{nameof(request.Lines)}[{index}]";
            Require(errors, line.CarrierBookingReference is { Length: <= MaxReferenceLength }, field, $"The carrierBookingReference must be at most {MaxReferenceLength} characters.");
            Require(errors, decimal.Round(line.Amount, 2) == line.Amount, field, "The amount must be in cents.");
            Require(errors, line.Description is null or { Length: <= MaxDescriptionLength }, field, $"The description must be at most {MaxDescriptionLength} characters.");
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (
            new CarrierInvoiceData(
                new CarrierId(request.CarrierId),
                request.InvoiceNumber,
                request.InvoiceDate,
                [.. request.Lines.Select(line => new CarrierInvoiceLineData(line.CarrierBookingReference, line.Amount, line.Description))],
                request.CorrelationId),
            errors);
    }

    private static void Require(Dictionary<string, string[]> errors, bool condition, string field, string message)
    {
        if (!condition)
        {
            errors[field] = [message];
        }
    }
}
