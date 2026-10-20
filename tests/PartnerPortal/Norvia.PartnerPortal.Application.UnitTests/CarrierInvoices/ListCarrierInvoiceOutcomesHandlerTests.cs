using Ardalis.SharedKernel;
using Norvia.PartnerPortal.Application.CarrierInvoices.Outcomes;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

public sealed class ListCarrierInvoiceOutcomesHandlerTests
{
    private readonly IReadRepository<CarrierInvoiceOutcome> _outcomes = Substitute.For<IReadRepository<CarrierInvoiceOutcome>>();

    [Fact]
    public async Task Handle_ListsTheOutcomesNorviaToldThePersona_WhetherOrNotItSentTheInvoiceFromThePortal()
    {
        var told = CarrierInvoiceSamples.Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Matched));
        _outcomes.ListAsync(Arg.Any<CarrierInvoiceOutcomesOfPersonaSpec>(), Arg.Any<CancellationToken>()).Returns([told]);

        var result = await new ListCarrierInvoiceOutcomesHandler(_outcomes).Handle(new ListCarrierInvoiceOutcomesQuery(CarrierInvoiceSamples.Persona), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeSameAs(told);
    }
}
