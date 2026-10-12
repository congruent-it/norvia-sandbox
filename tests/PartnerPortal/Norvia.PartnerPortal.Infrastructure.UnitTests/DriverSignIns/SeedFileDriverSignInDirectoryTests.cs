using System.Text;
using Norvia.PartnerPortal.Infrastructure.DriverSignIns;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.DriverSignIns;

public sealed class SeedFileDriverSignInDirectoryTests
{
    private static SeedFileDriverSignInDirectory Read(DriverSignInOptions options, string? seed = null) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(seed ?? Contracts.Read("seed/norvia.seed.json"))), options);

    [Fact]
    public void Current_WithADomain_GivesEveryActiveDriverTheirTestUser_AndThePassword()
    {
        var current = Read(new DriverSignInOptions { Domain = "norvia.example", Password = "s3cret" }).Current;

        current.Password.Should().Be("s3cret");
        current.Drivers.Should().Contain(new Application.DriverSignIns.DriverSignInView("DRV-0001", "Anouk de Vries", "drv-0001@norvia.example"));
    }

    [Fact]
    public void Current_WithoutADomain_HasNoTestUsersAndNoPassword()
    {
        var current = Read(new DriverSignInOptions { Password = "left over" }).Current;

        current.Password.Should().BeNull();
        current.Drivers.Should().NotBeEmpty().And.OnlyContain(driver => driver.UserName == null);
    }

    [Fact]
    public void Current_LeavesOutDriversWhoAreNotActive()
    {
        var seed = """{ "drivers": [ { "id": "DRV-0001", "name": "A", "active": true }, { "id": "DRV-0002", "name": "B", "active": false } ] }""";

        Read(new DriverSignInOptions { Domain = "norvia.example" }, seed).Current.Drivers.Select(driver => driver.DriverId).Should().Equal("DRV-0001");
    }
}
