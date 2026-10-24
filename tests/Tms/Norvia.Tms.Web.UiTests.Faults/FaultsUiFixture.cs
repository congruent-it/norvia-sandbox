using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using Norvia.AppHost;

namespace Norvia.Tms.Web.UiTests.Faults;

/// <summary>
/// The TMS with what a presenter shows beside its faults: the Partner portal, whose presenter panel switches them, the Driver app with its screens, the workers that play the Trip dispatch and
/// Driver reports Logic Apps, and the TMS UI. A headless Chromium drives the three UIs. The Driver reports worker retries for two minutes, longer than a journey keeps the TMS down, so the
/// Driver's report waits in its session and goes through once the TMS is back. A fault is on for the whole application, so the tests run one at a time.
/// </summary>
public sealed class FaultsUiFixture : TmsFixture
{
    public const string FirstDriver = "DRV-0001";
    public const string SecondDriver = "DRV-0002";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private readonly string _seedPath = Path.Combine(Path.GetTempPath(), $"tms.faults-ui-seed.{Guid.NewGuid():N}.json");
    private IPlaywright? _playwright;

    /// <summary>The Partner portal, whose presenter panel switches the faults.</summary>
    public Uri PartnerPortal { get; private set; } = null!;

    public Uri DriverApp { get; private set; } = null!;

    public Uri TmsWeb { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>The Driver delivers the Trip's Shipment through the Driver app's API with a signature and one photo, both the given PNG (base64).</summary>
    public async Task DeliverAsync(DispatchedTrip trip, string png)
    {
        using var driver = App.CreateHttpClient("driver-app-api");
        driver.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", IssueToken([trip.DriverId], audience: LocalTokens.DriverAppAudience, clientId: trip.DriverId));
        var file = new { contentType = "image/png", content = png };
        using var response = await driver.PostAsJsonAsync(
            $"/v1/driver/trips/{trip.TripNumber}/stops/{trip.ShipmentNumber}/delivery/deliver",
            new { receivedBy = "Consignee", condition = "Clean", signature = file, photos = new[] { file } },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public override async ValueTask InitializeAsync()
    {
        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        await base.InitializeAsync();
        foreach (var resource in new[] { "tms-web", "partner-portal-web", "driver-app-api", "driver-app-web" })
        {
            await WaitForHealthyAsync(resource, CancellationToken.None);
        }

        foreach (var worker in new[] { "trip-dispatch-worker", "driver-reports-worker" })
        {
            await App.ResourceNotifications.WaitForResourceAsync(worker, KnownResourceStates.Running, CancellationToken.None);
        }

        PartnerPortal = App.GetEndpoint("partner-portal-web", "http");
        DriverApp = App.GetEndpoint("driver-app-web", "http");
        TmsWeb = App.GetEndpoint("tms-web", "http");
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await base.DisposeAsync();
        File.Delete(_seedPath);
        GC.SuppressFinalize(this);
    }

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheTmsFaultUiTestsDontUse();

        var interval = RetryInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "driver-reports-worker"))
            .WithEnvironment("DriverReports__Retry__Count", "60")
            .WithEnvironment("DriverReports__Retry__Interval", interval)
            .WithEnvironment("DriverReports__Retry__MaximumInterval", interval);
    }

    /// <summary>The test seed, with a shift at the weekend for the two Drivers the tests use, so a Trip can be planned for any day the tests run on.</summary>
    protected override Dictionary<string, string> TmsApiSettings()
    {
        TestSeed.WriteWithWeekendShifts(_seedPath, FirstDriver, SecondDriver);
        var settings = base.TmsApiSettings();
        settings["Seed__Path"] = _seedPath;
        return settings;
    }
}
