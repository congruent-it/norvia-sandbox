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

    [Fact]
    public void Views_OfCustomerService_AreTheRejectedBookingsThenTheRatingExceptions()
    {
        NorviaRole.CustomerService.Views.Select(view => (view.Path, view.Title)).Should().Equal(
            ("/rejected-bookings", "Rejected bookings"),
            ("/rating-exceptions", "Rating exceptions"));
    }

    [Fact]
    public void Views_OfPlanner_AreWaitingForPlanningThenShipmentsToReleaseThenBookingsAwaitingAnAnswer()
    {
        NorviaRole.Planner.Views.Select(view => (view.Path, view.Title)).Should().Equal(
            ("/transport-orders", "Waiting for planning"),
            ("/shipments/to-release", "Shipments to release"),
            ("/shipments/awaiting-answer", "Bookings awaiting an answer"));
    }

    [Fact]
    public void Home_IsTheRolesFirstView()
    {
        NorviaRole.CustomerService.Home.Should().Be("/rejected-bookings");
        NorviaRole.Planner.Home.Should().Be("/transport-orders");
    }
}
