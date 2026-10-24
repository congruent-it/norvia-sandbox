using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Norvia.ServiceDefaults.Faults;
using Norvia.Tms.Api.Faults;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The TMS's faults gate the calls they break: off by default, answered with a 503 while on, and let go of when they expire (P5, P8, P12).</summary>
public sealed class TmsFaultMiddlewareTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeLogger<TmsFaultMiddleware> _logger = new();
    private readonly ServiceProvider _services;
    private readonly IFaults _faults;
    private bool _reachedTheEndpoint;

    public TmsFaultMiddlewareTests()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([]);
        builder.AddFaults(TmsFaultDefinitions.All);
        builder.Services.AddSingleton<TimeProvider>(_time);
        _services = builder.Services.BuildServiceProvider();
        _faults = _services.GetRequiredService<IFaults>();
    }

    public void Dispose() => _services.Dispose();

    private async Task<DefaultHttpContext> CallAsync(string path, string method = "POST")
    {
        _reachedTheEndpoint = false;
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var middleware = new TmsFaultMiddleware(_ =>
        {
            _reachedTheEndpoint = true;
            return Task.CompletedTask;
        }, _faults, _logger);

        await middleware.InvokeAsync(context);
        return context;
    }

    private static string BodyOf(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    [Theory]
    [InlineData("/v1/tracking-events")]
    [InlineData("/v1/shipper-tracking/transport-orders")]
    [InlineData("/v1/trips/TR-0000012/start")]
    [InlineData("/v1/proofs-of-delivery")]
    public async Task WithNoFaultOn_EveryCallGoesThrough(string path)
    {
        var context = await CallAsync(path);

        _reachedTheEndpoint.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        _logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public void EveryFault_IsOffByDefault_AndOwnedByTheTms()
    {
        _faults.List().Should().HaveCount(3).And.OnlyContain(fault => !fault.On && fault.Owner == "tms");
    }

    [Theory]
    [InlineData("/v1/tracking-events")]
    [InlineData("/v1/shipper-tracking/transport-orders")]
    [InlineData("/v1/trips/TR-0000012/start")]
    [InlineData("/v1/proofs-of-delivery")]
    [InlineData("/v1/shipments/SH-0000001/release")]
    public async Task WhileTheTmsIsUnavailable_EveryApiCallAnswers503_AndReachesNothing(string path)
    {
        _faults.SwitchOn(TmsFaultNames.Unavailable, null, out _);

        var context = await CallAsync(path);

        _reachedTheEndpoint.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.ContentType.Should().Be("application/problem+json");
        BodyOf(context).Should().Contain("The TMS is unavailable.");
    }

    [Fact]
    public async Task WhileTheTmsIsUnavailable_TheHopIsFailed_WithTheFaultAsItsKey()
    {
        _faults.SwitchOn(TmsFaultNames.Unavailable, null, out _);

        await CallAsync("/v1/trips/TR-0000012/start");

        var log = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        log.Level.Should().Be(LogLevel.Warning);
        var keys = log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary(pair => pair.Key, pair => pair.Value);
        keys.Should().Contain(new Dictionary<string, object?> { ["outcome"] = "Failed", ["failureCode"] = "TmsUnavailable", ["fault"] = "tms-unavailable" });
    }

    [Theory]
    [InlineData("/v1/faults")]
    [InlineData("/v1/faults/tms-unavailable")]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/openapi/v1.json")]
    public async Task WhileTheTmsIsUnavailable_TheFaultSwitchesTheHealthChecksAndTheOpenApiDocumentAreNotGated(string path)
    {
        _faults.SwitchOn(TmsFaultNames.Unavailable, null, out _);

        var context = await CallAsync(path, "PUT");

        _reachedTheEndpoint.Should().BeTrue("a fault can always be switched off, and the platform still sees the TMS is up");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task WhileTheTrackingEventApiFails_OnlyItsCallsAnswer503()
    {
        _faults.SwitchOn(TmsFaultNames.TrackingEventApiFailing, null, out _);

        var tracking = await CallAsync("/v1/tracking-events");
        var trackingLog = _logger.Collector.GetSnapshot().Single();
        var others = new[] { "/v1/shipper-tracking/transport-orders", "/v1/proofs-of-delivery", "/v1/trips/TR-0000012/start" };

        tracking.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        BodyOf(tracking).Should().Contain("The TMS Tracking event API is unavailable.");
        Keys(trackingLog).Should().Contain(new Dictionary<string, object?> { ["failureCode"] = "TrackingEventApiUnavailable", ["fault"] = "tms-tracking-event-api-failing" });
        foreach (var path in others)
        {
            (await CallAsync(path)).Response.StatusCode.Should().Be(StatusCodes.Status200OK, path);
            _reachedTheEndpoint.Should().BeTrue(path);
        }
    }

    [Fact]
    public async Task WhileTheShipperTrackingApiFails_OnlyItsCallsAnswer503_AtOnce()
    {
        _faults.SwitchOn(TmsFaultNames.ShipperTrackingApiFailing, null, out _);

        var shipper = await CallAsync("/v1/shipper-tracking/transport-orders/TO-0000001", "GET");
        var shipperLog = _logger.Collector.GetSnapshot().Single();
        var tracking = await CallAsync("/v1/tracking-events");

        shipper.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        BodyOf(shipper).Should().Contain("The TMS Shipper tracking API is unavailable.");
        Keys(shipperLog).Should().Contain(new Dictionary<string, object?> { ["failureCode"] = "ShipperTrackingApiUnavailable", ["fault"] = "tms-shipper-tracking-api-failing" });
        tracking.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task WhenTheTmsIsUnavailableAndAnApiFails_TheHopNamesTheMoreSpecificFault()
    {
        _faults.SwitchOn(TmsFaultNames.Unavailable, null, out _);
        _faults.SwitchOn(TmsFaultNames.TrackingEventApiFailing, null, out _);

        await CallAsync("/v1/tracking-events");

        Keys(_logger.Collector.GetSnapshot().Single()).Should().Contain("fault", "tms-tracking-event-api-failing");
    }

    [Theory]
    [InlineData(TmsFaultNames.Unavailable, "/v1/trips/TR-0000012/start")]
    [InlineData(TmsFaultNames.TrackingEventApiFailing, "/v1/tracking-events")]
    [InlineData(TmsFaultNames.ShipperTrackingApiFailing, "/v1/shipper-tracking/transport-orders")]
    public async Task AFault_ExpiresByItself_AndTheCallsGoThroughAgain(string fault, string path)
    {
        _faults.SwitchOn(fault, TimeSpan.FromMinutes(2), out _);
        (await CallAsync(path)).Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

        _time.Advance(TimeSpan.FromMinutes(2));

        (await CallAsync(path)).Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        _reachedTheEndpoint.Should().BeTrue();
    }

    [Fact]
    public async Task AFaultSwitchedOff_LetsTheCallsThroughAtOnce()
    {
        _faults.SwitchOn(TmsFaultNames.Unavailable, null, out _);
        _faults.SwitchOff(TmsFaultNames.Unavailable, out _);

        (await CallAsync("/v1/trips/TR-0000012/start")).Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private static Dictionary<string, object?> Keys(FakeLogRecord log) =>
        log.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope).ToDictionary(pair => pair.Key, pair => pair.Value);
}
