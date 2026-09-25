using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Web.Roles;

namespace Norvia.Tms.Web.UnitTests.Roles;

public sealed class RoleSwitcherTests : BunitContext
{
    public RoleSwitcherTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgeryState());
    }

    [Fact]
    public void Render_OffersEveryRoleAndMarksTheCurrentOne()
    {
        var switcher = Render<RoleSwitcher>(parameters => parameters.Add(s => s.Current, NorviaRole.CustomerService));

        var buttons = switcher.FindAll("form[action='/role'][method='post'] button[name='role']");
        buttons.Select(button => (button.TextContent.Trim(), button.GetAttribute("value"), button.GetAttribute("aria-pressed")))
            .Should().Equal(
                ("Planner", "planner", "false"),
                ("Customer service", "customer-service", "true"));
    }

    [Fact]
    public void Render_ProtectsTheSwitchAgainstForgery()
    {
        var switcher = Render<RoleSwitcher>(parameters => parameters.Add(s => s.Current, NorviaRole.Planner));

        switcher.Find("form input[type=hidden][name='__RequestVerificationToken']").GetAttribute("value").Should().Be("token");
    }

    private sealed class FixedAntiforgeryState : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("token", "__RequestVerificationToken");
    }
}
