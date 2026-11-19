using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalConsigneeNotificationWorker.UnitTests;

/// <summary>The flow with the TMS and the mail server stood in for, and a clock the test moves.</summary>
internal sealed class ConsigneeNotificationWorld
{
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";
    public const string Email = "inkoop@dezwaan.example";
    public const string Code = "7QK2M9XH4D8VWRBT";

    public ConsigneeNotificationWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Tms = new StubHttp(Time);
        Tms.Otherwise(HttpStatusCode.OK, Content);
        Options = Microsoft.Extensions.Options.Options.Create(new ConsigneeNotificationWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "audience", ClientId = "consignee-notification-worker", SigningKey = Signing },
            PartnerPortalBaseAddress = new Uri("https://portal.norvia.example/"),
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });
        Flow = new ConsigneeNotificationFlow(
            new TmsConsigneeNotifications(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time, new FakeLogger<TmsConsigneeNotifications>()),
            Mailbox, Options, Time, Logger);
    }

    public static string Content => $$"""{"transportOrderNumber":"TO-0000005","email":"{{Email}}","trackingCode":"{{Code}}"}""";

    public FakeTimeProvider Time { get; }

    public StubHttp Tms { get; }

    public RecordingMailbox Mailbox { get; } = new();

    public FakeLogger<ConsigneeNotificationFlow> Logger { get; } = new();

    public IOptions<ConsigneeNotificationWorkerOptions> Options { get; }

    public ConsigneeNotificationFlow Flow { get; }

    public static string Request(string reason = "Collected") => $$"""
        {
          "eventId": "7d1e0c4a-5b2f-4e8a-9c3d-0a1b2c3d4e05",
          "eventType": "ConsigneeNotificationRequested",
          "eventVersion": 1,
          "occurredAt": "2030-01-10T08:00:00+00:00",
          "correlationId": "collected-5",
          "transportOrderNumber": "TO-0000005",
          "shipmentNumber": "SH-0000045",
          "reason": "{{reason}}"
        }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<ConsigneeNotificationFlowOutcome> RunAsync(string request)
    {
        var run = Flow.RunAsync(request, CancellationToken.None);
        while (!run.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await run;
    }
}

/// <summary>A mail server that keeps what it was given and answers as the test says.</summary>
internal sealed class RecordingMailbox : IConsigneeMailbox
{
    private readonly Queue<MailAnswer> _answers = new();

    public List<JsonObject> Sent { get; } = [];

    public List<DateTimeOffset> Attempts { get; } = [];

    public MailAnswer Otherwise { get; set; } = new(true, false);

    public RecordingMailbox Then(MailAnswer answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    public Task<MailAnswer> SendAsync(JsonObject email, CancellationToken cancellationToken)
    {
        Sent.Add(email);
        return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : Otherwise);
    }
}
