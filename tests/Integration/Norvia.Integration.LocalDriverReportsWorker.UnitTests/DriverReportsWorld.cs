using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Norvia.Integration.LocalDriverReportsWorker.UnitTests;

/// <summary>The flow with the TMS and the Driver app stood in for, and a clock the test moves.</summary>
internal sealed class DriverReportsWorld
{
    public const string TripNumber = "TR-0000012";
    public const string EventId = "0198e7c0-0000-7000-8000-0000000000c1";
    public const string CorrelationId = "0198e7c0-0000-7000-8000-0000000000c2";
    public const string Signing = "c2lnbmluZy1rZXktZm9yLXRoZS11bml0LXRlc3RzLXNpZ25pbmcta2V5LTEyMzQ1Njc4OTA=";

    public DriverReportsWorld(int retryCount = 3)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2030, 10, 1, 8, 0, 0, TimeSpan.Zero));
        Tms = new StubHttp(Time);
        DriverApp = new StubHttp(Time);
        Options = Microsoft.Extensions.Options.Options.Create(new DriverReportsWorkerOptions
        {
            TmsBaseAddress = new Uri("http://tms.test"),
            TmsTokens = new() { Issuer = "issuer", Audience = "tms", ClientId = "driver-reports-worker", SigningKey = Signing },
            DriverApp = new()
            {
                BaseAddress = new Uri("http://driver-app.test"),
                Tokens = new() { Issuer = "issuer", Audience = "driver-app", ClientId = "driver-reports-worker", SigningKey = Signing },
            },
            Retry = new() { Count = retryCount, Interval = TimeSpan.FromSeconds(5), MaximumInterval = TimeSpan.FromSeconds(20) },
        });

        Flow = new DriverReportsFlow(
            new TmsTripCalls(new HttpClient(Tms) { BaseAddress = Options.Value.TmsBaseAddress }, Options, Time),
            ProofFiles,
            new DriverAppOutcomes(new HttpClient(DriverApp) { BaseAddress = Options.Value.DriverApp.BaseAddress }, Options, Time),
            Options, Time, NullLogger<DriverReportsFlow>.Instance);
    }

    public FakeTimeProvider Time { get; }

    public StubHttp Tms { get; }

    public StubHttp DriverApp { get; }

    /// <summary>The Driver app's transit container: the files it holds, by blob reference, and what the flow deleted.</summary>
    public FakeProofFiles ProofFiles { get; } = new();

    public IOptions<DriverReportsWorkerOptions> Options { get; }

    public DriverReportsFlow Flow { get; }

    /// <summary>How the run settled the message, and how many calls the Driver app had by then: the message is settled before the outcome is reported.</summary>
    public List<(Settlement Settlement, int DriverAppCalls)> Settled { get; } = [];

    public static string StartReported(string driverId = "DRV-0002") => $$"""
        {
          "eventId": "{{EventId}}",
          "eventType": "TripStartReported",
          "eventVersion": 1,
          "occurredAt": "2030-10-03T08:05:12+02:00",
          "correlationId": "{{CorrelationId}}",
          "tripNumber": "{{TripNumber}}",
          "version": 2,
          "driverId": "{{driverId}}"
        }
        """;

    public const string EndEventId = "0198e7c0-0000-7000-8000-0000000000f1";
    public const string EndCorrelationId = "0198e7c0-0000-7000-8000-0000000000f2";

    public static string EndReported(string driverId = "DRV-0002") => $$"""
        {
          "eventId": "{{EndEventId}}",
          "eventType": "TripEndReported",
          "eventVersion": 1,
          "occurredAt": "2030-10-03T16:42:05+02:00",
          "correlationId": "{{EndCorrelationId}}",
          "tripNumber": "{{TripNumber}}",
          "version": 3,
          "driverId": "{{driverId}}"
        }
        """;

    public const string StopEventId = "0198e7c0-0000-7000-8000-0000000000d1";
    public const string StopCorrelationId = "0198e7c0-0000-7000-8000-0000000000d2";

    public static string StopReported(string trackingEventType = "Collected", string stop = "Collection", string? remark = null, string driverId = "DRV-0002") => $$"""
        {
          "eventId": "{{StopEventId}}",
          "eventType": "StopCompletedReported",
          "eventVersion": 1,
          "occurredAt": "2030-10-03T08:41:03+02:00",
          "correlationId": "{{StopCorrelationId}}",
          "tripNumber": "{{TripNumber}}",
          "version": 3,
          "driverId": "{{driverId}}",
          "shipmentNumber": "SH-0000031",
          "stop": "{{stop}}",
          "trackingEventType": "{{trackingEventType}}",
          "location": { "place": "Rotterdam", "postcode": "3011 AA", "countryCode": "NL" }{{(remark is null ? "" : $$""", "remark": "{{remark}}" """)}}
        }
        """;

    public const string ProofEventId = "0198e7c0-0000-7000-8000-0000000000e2";
    public const string ProofCorrelationId = "0198e7c0-0000-7000-8000-0000000000e3";

    public static string[] ProofBlobs { get; } = [$"{ProofEventId}/signature.png", $"{ProofEventId}/photo-1.jpg"];

    public static string ProofCaptured(string condition = "Clean", string? remark = null, string driverId = "DRV-0002") => $$"""
        {
          "eventId": "{{ProofEventId}}",
          "eventType": "ProofOfDeliveryCaptured",
          "eventVersion": 1,
          "occurredAt": "2030-10-03T10:12:30+02:00",
          "correlationId": "{{ProofCorrelationId}}",
          "tripNumber": "{{TripNumber}}",
          "version": 3,
          "driverId": "{{driverId}}",
          "shipmentNumber": "SH-0000031",
          "documentId": "{{ProofEventId}}",
          "receivedBy": "J. Bakker",
          "deliveryTime": "2030-10-03T10:12:30+02:00",
          "location": { "place": "Utrecht", "postcode": "3511 AB", "countryCode": "NL" },
          "condition": "{{condition}}"{{(remark is null ? "" : $$""", "remark": "{{remark}}" """)}},
          "files": [
            { "name": "signature.png", "contentType": "image/png", "blobReference": "{{ProofBlobs[0]}}" },
            { "name": "photo-1.jpg", "contentType": "image/jpeg", "blobReference": "{{ProofBlobs[1]}}" }
          ]
        }
        """;

    public static string Problem(int status, string code, string detail) => $$"""
        { "type": "about:blank", "title": "The Trip can't be started.", "status": {{status}}, "detail": "{{detail}}", "code": "{{code}}", "correlationId": "{{CorrelationId}}" }
        """;

    /// <summary>Runs the flow, moving the clock on whenever it waits to retry.</summary>
    public async Task<DriverReportsRunOutcome> RunAsync(string message)
    {
        var run = Flow.RunAsync(
            message,
            (settlement, _) =>
            {
                Settled.Add((settlement, DriverApp.Requests.Count));
                return Task.CompletedTask;
            },
            CancellationToken.None);
        while (!run.IsCompleted)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        return await run;
    }

    public JsonObject OutcomeReported(int index = 0) => JsonNode.Parse(DriverApp.Requests[index].Body)!.AsObject();

    /// <summary>The app role in the bearer token of a request.</summary>
    public static string RoleOf(StubHttp.Request request)
    {
        var token = request.Headers["Authorization"]["Bearer ".Length..];
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))!["roles"]!.AsArray().Single()!.GetValue<string>();
    }

    public static StubHttp Answering(StubHttp http, HttpStatusCode status, string body = "") => http.Otherwise(status, body);
}

/// <summary>The transit container, stood in for: the files it holds by blob reference, and what was deleted from it.</summary>
internal sealed class FakeProofFiles : IDriverProofFiles
{
    public Dictionary<string, byte[]> Held { get; } = new()
    {
        [DriverReportsWorld.ProofBlobs[0]] = [0x89, 0x50, 0x4E, 0x47, 1],
        [DriverReportsWorld.ProofBlobs[1]] = [0xFF, 0xD8, 0xFF, 2],
    };

    public List<string> Deleted { get; } = [];

    public Task<IReadOnlyList<FetchedProofFile>?> FetchAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FetchedProofFile>?>(files.All(file => Held.ContainsKey(file.BlobReference))
            ? [.. files.Select(file => new FetchedProofFile(file.Name, file.ContentType, Held[file.BlobReference]))]
            : null);

    public Task DeleteAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken)
    {
        Deleted.AddRange(files.Select(file => file.BlobReference));
        return Task.CompletedTask;
    }
}
