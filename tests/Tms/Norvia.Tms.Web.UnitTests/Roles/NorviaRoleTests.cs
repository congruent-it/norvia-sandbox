using Norvia.Tms.Web.Roles;

namespace Norvia.Tms.Web.UnitTests.Roles;

public sealed class NorviaRoleTests
{
    [Theory]
    [InlineData("planner", "Planner")]
    [InlineData("customer-service", "Customer service")]
    public void Find_AKnownKey_IsThatRole(string key, string name)
    {
        NorviaRole.Find(key)!.Name.Should().Be(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Planner")]
    [InlineData("administrator")]
    public void Find_AnythingElse_IsNoRole(string? key)
    {
        NorviaRole.Find(key).Should().BeNull();
    }

    [Fact]
    public void All_OffersThePlannerFirst()
    {
        NorviaRole.All.Select(role => role.Name).Should().Equal("Planner", "Customer service");
    }
}
