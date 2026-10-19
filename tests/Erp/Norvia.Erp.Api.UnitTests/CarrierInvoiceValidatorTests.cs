using Norvia.Erp.Api.CarrierInvoices;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CarrierInvoiceValidatorTests
{
    internal static CarrierInvoiceRequest Valid => new(
        "CAR-0001",
        "VDT-2026-0412",
        new DateOnly(2026, 10, 19),
        "invoice-1",
        [new CarrierInvoiceLineRequest("TRX-998", 70.50m, "Utrecht"), new CarrierInvoiceLineRequest("TRX-999", 20m)]);

    [Fact]
    public void Validate_AValidInvoice_MapsToWhatTheDomainTakes()
    {
        var (invoice, errors) = CarrierInvoiceValidator.Validate(Valid);

        errors.Should().BeEmpty();
        invoice!.CarrierId.Value.Should().Be("CAR-0001");
        invoice.InvoiceNumber.Should().Be("VDT-2026-0412");
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 10, 19));
        invoice.CorrelationId.Should().Be("invoice-1");
        invoice.Lines.Select(line => (line.CarrierBookingReference, line.Amount, line.Description)).Should().Equal(("TRX-998", 70.50m, "Utrecht"), ("TRX-999", 20m, null));
    }

    [Fact]
    public void Validate_NoBody_IsRefused()
    {
        var (invoice, errors) = CarrierInvoiceValidator.Validate(null);

        invoice.Should().BeNull();
        errors.Should().ContainKey(string.Empty);
    }

    [Fact]
    public void Validate_AnInvoiceWithoutLines_IsLeftToTheDomainsReceivingRules()
    {
        var (invoice, errors) = CarrierInvoiceValidator.Validate(Valid with { Lines = [] });

        errors.Should().BeEmpty();
        invoice!.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_ABlankCarrierId_IsRefused(string carrierId)
    {
        var (invoice, errors) = CarrierInvoiceValidator.Validate(Valid with { CarrierId = carrierId });

        invoice.Should().BeNull();
        errors.Should().ContainKey("CarrierId");
    }

    [Fact]
    public void Validate_ABlankCorrelationId_IsRefused() =>
        CarrierInvoiceValidator.Validate(Valid with { CorrelationId = " " }).Errors.Should().ContainKey("CorrelationId");

    [Fact]
    public void Validate_ABlankOrTooLongInvoiceNumber_IsRefused()
    {
        CarrierInvoiceValidator.Validate(Valid with { InvoiceNumber = "" }).Errors.Should().ContainKey("InvoiceNumber");
        CarrierInvoiceValidator.Validate(Valid with { InvoiceNumber = new string('A', 51) }).Errors.Should().ContainKey("InvoiceNumber");
    }

    [Fact]
    public void Validate_ALineBeyondTheSchema_IsRefusedNamingTheLine()
    {
        var long200 = new string('R', 201);

        CarrierInvoiceValidator.Validate(Valid with { Lines = [new CarrierInvoiceLineRequest(long200, 1m)] }).Errors.Should().ContainKey("Lines[0]");
        CarrierInvoiceValidator.Validate(Valid with { Lines = [new CarrierInvoiceLineRequest("TRX-1", 1.005m)] }).Errors.Should().ContainKey("Lines[0]");
        CarrierInvoiceValidator.Validate(Valid with { Lines = [new CarrierInvoiceLineRequest("TRX-1", 1m, new string('d', 501))] }).Errors.Should().ContainKey("Lines[0]");
    }

    [Fact]
    public void Validate_BlankReferenceAndNonPositiveAmount_ArePassedOnForTheDomainToRefuseWithItsCodes()
    {
        var (invoice, errors) = CarrierInvoiceValidator.Validate(Valid with { Lines = [new CarrierInvoiceLineRequest("", 0m)] });

        errors.Should().BeEmpty();
        invoice.Should().NotBeNull();
    }
}
