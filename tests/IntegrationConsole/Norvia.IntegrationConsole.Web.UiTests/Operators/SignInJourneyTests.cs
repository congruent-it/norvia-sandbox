using System.Net;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.IntegrationConsole.Web.UiTests.Operators;

public sealed class SignInJourneyTests(ConsoleUiFixture console) : BrowserPage(console)
{
    [Fact]
    public async Task AnOperatorWithTheReadRole_LandsOnTheSearchPage()
    {
        await SignInAsync("Iris Operator", "IntegrationConsole.Read");

        await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Search Message trails" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" })).ToContainTextAsync("Iris Operator");
    }

    [Fact]
    public async Task AUserWithoutTheReadRole_IsRefused()
    {
        // The Resubmit role alone isn't enough: every page shows what Read allows.
        await SignInAsync("Sam Stranger", "IntegrationConsole.Resubmit");

        var response = await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You can't use the Integration console" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("IntegrationConsole.Read", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Search Message trails" })).ToHaveCountAsync(0);
        response!.Status.Should().Be((int)HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ABrowserNobodySignedIn_IsSentToEntra()
    {
        // Easy Auth signs a browser in before it reaches the console; if a session ends, the console sends it back there.
        using var browser = WithoutFollowingRedirects();

        using var response = await browser.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/.auth/login/aad?post_login_redirect_uri=");
    }

    [Fact]
    public async Task TheHealthProbes_NeedNoSignIn()
    {
        using var probe = WithoutFollowingRedirects();

        using var response = await probe.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // On Kestrel the factory's own client follows redirects whatever its options say.
    private HttpClient WithoutFollowingRedirects() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = Console.Address };
}
