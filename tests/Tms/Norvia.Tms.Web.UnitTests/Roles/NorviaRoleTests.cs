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
    public void Views_OfCustomerService_AreTheRejectedBookingsThenTheRatingExceptionsThenNotYetBillable()
    {
        NorviaRole.CustomerService.Views.Select(view => (view.Path, view.Title)).Should().Equal(
            ("/rejected-bookings", "Rejected bookings"),
            ("/transport-orders/all", "Transport Orders"),
            ("/rating-exceptions", "Rating exceptions"),
            ("/not-yet-billable", "Not yet billable"));
    }

    [Fact]
    public void Views_OfPlanner_AreGroupedByWhatThePlannerDoesWithThem()
    {
        NorviaRole.Planner.Views.Select(view => (view.Path, view.Title, view.Group)).Should().Equal(
            ("/transport-orders", "Waiting for planning", "Plan"),
            ("/transport-orders/all", "Transport Orders", "Plan"),
            ("/shipments/to-release", "Shipments to release", "Plan"),
            ("/shipments/awaiting-answer", "Bookings awaiting an answer", "Follow"),
            ("/shipments/in-progress", "Shipments in progress", "Follow"),
            ("/shipments/delivered-without-proof-of-delivery", "Delivered without Proof of delivery", "Follow"),
            ("/exceptions", "Open Exceptions", "Follow"),
            ("/capacity", "Capacity", "Own fleet"),
            ("/trips/shipments", "Shipments to put on Trips", "Own fleet"),
            ("/trips", "Trips", "Own fleet"));
    }

    [Fact]
    public void Views_OfCustomerService_AreInNoGroup()
    {
        NorviaRole.CustomerService.Views.Should().OnlyContain(view => view.Group == null);
    }

    [Fact]
    public void Home_OfThePlanner_IsItsHomePage()
    {
        NorviaRole.Planner.Home.Should().Be("/home");
    }

    [Fact]
    public void Home_OfCustomerService_IsItsFirstView()
    {
        NorviaRole.CustomerService.HomePath.Should().BeNull();
        NorviaRole.CustomerService.Home.Should().Be("/rejected-bookings");
    }
}
