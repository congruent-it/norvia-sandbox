using Norvia.DriverApp.Application.Drivers;
using Norvia.DriverApp.Application.Drivers.List;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Drivers;

public sealed class ListDriversHandlerTests
{
    [Fact]
    public async Task ItListsTheDriversTheDirectoryHas()
    {
        var directory = Substitute.For<IDriverDirectory>();
        directory.All.Returns([new DriverView("DRV-0001", "Anouk de Vries")]);

        var drivers = await new ListDriversHandler(directory).Handle(new ListDriversQuery(), CancellationToken.None);

        drivers.Should().Equal(new DriverView("DRV-0001", "Anouk de Vries"));
    }
}
