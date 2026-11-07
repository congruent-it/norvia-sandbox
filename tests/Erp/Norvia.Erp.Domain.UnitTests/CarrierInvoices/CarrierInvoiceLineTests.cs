using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Domain.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceLineTests
{
    private static CarrierInvoiceLine Line(CarrierInvoiceLineResult result, CarrierInvoiceLineStatus status) =>
        new("TRX-1", 80m, null, result, status, null);

    [Theory]
    [InlineData(CarrierInvoiceLineResult.UnknownBooking, CarrierInvoiceLineStatus.ToDecide, true)]
    [InlineData(CarrierInvoiceLineResult.UnknownBooking, CarrierInvoiceLineStatus.Rejected, false)]
    [InlineData(CarrierInvoiceLineResult.PriceMismatch, CarrierInvoiceLineStatus.ToDecide, false)]
    [InlineData(CarrierInvoiceLineResult.Duplicate, CarrierInvoiceLineStatus.ToDecide, false)]
    [InlineData(CarrierInvoiceLineResult.Matched, CarrierInvoiceLineStatus.Matched, false)]
    public void ResolvesAutomatically_OnlyAnUnknownBookingThatStillWaitsForFinance(CarrierInvoiceLineResult result, CarrierInvoiceLineStatus status, bool expected) =>
        Line(result, status).ResolvesAutomatically.Should().Be(expected);
}
