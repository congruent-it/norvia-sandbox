using Norvia.PartnerPortal.Application.DriverSignIns;
using Norvia.PartnerPortal.Application.DriverSignIns.List;

namespace Norvia.PartnerPortal.Application.UnitTests.DriverSignIns;

public sealed class ListDriverSignInsHandlerTests
{
    [Fact]
    public async Task Handle_GivesTheSignInDetailsOfTheSeededDrivers()
    {
        var details = new DriverSignInsView("s3cret", [new DriverSignInView("DRV-0001", "Anouk de Vries", "drv-0001@norvia.example")]);
        var signIns = Substitute.For<IDriverSignInDirectory>();
        signIns.Current.Returns(details);

        var listed = await new ListDriverSignInsHandler(signIns).Handle(new ListDriverSignInsQuery(), CancellationToken.None);

        listed.Should().Be(details);
    }
}
