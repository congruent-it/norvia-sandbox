using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Integration.ConsigneeNotification.IntegrationTests;

[assembly: AssemblyFixture(typeof(ConsigneeNotificationFixture))]

namespace Norvia.Integration.ConsigneeNotification.IntegrationTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run with what the Consignee notification (#983) touches: SQL Server with the TMS's database, the Service Bus emulator, the TMS API and
/// the worker that plays the Consignee notification Logic App. The path is the real one, from the Carrier's Collected report to the email. Only the mail server is the machine's: the worker
/// writes each email as an .eml file to a folder of the fixture's, which the tests read. The Partner portal is not started, so the link's address is one the fixture gives the worker.
/// The worker's retry waits are short, so a send that fails is retried and gives up within seconds. The tests make records of their own and run one at a time.
/// </summary>
public sealed class ConsigneeNotificationFixture : TmsFixture
{
    public const string WorkerName = "consignee-notification-worker";
    public const string Subscription = "consignee-notification";
    public const string TrackingBaseAddress = "https://track.example.test/";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(3);

    private readonly string _mailFolder = Path.Combine(Path.GetTempPath(), $"norvia-consignee-mail-{Guid.NewGuid():N}");
    private readonly ConcurrentQueue<TmsLog> _workerLogs = new();
    private ServiceBusClient? _bus;

    /// <summary>What the worker has logged, in the order it was written, as the JSON console formatter wrote it with the keys of its scopes.</summary>
    public IEnumerable<TmsLog> WorkerLogs => _workerLogs;

    public override async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_mailFolder);
        await base.InitializeAsync();
        WatchWorkerLogs();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.ResourceNotifications.WaitForResourceAsync(WorkerName, KnownResourceStates.Running, timeout.Token);
        _bus = new ServiceBusClient(await ConnectionStringAsync("messaging", timeout.Token));
    }

    public override async ValueTask DisposeAsync()
    {
        if (_bus is not null)
        {
            await _bus.DisposeAsync();
        }

        await base.DisposeAsync();
        if (Directory.Exists(_mailFolder))
        {
            Directory.Delete(_mailFolder, recursive: true);
        }
    }

    protected override void ConfigureAppHost(IDistributedApplicationTestingBuilder builder)
    {
        builder.RemoveWhatTheConsigneeNotificationTestsDontUse();

        // The worker's retry waits are the workflow's 5 s up to 20 s, and a test waits for them, so they are short here. The count stays the workflow's three.
        builder.CreateResourceBuilder(builder.Resources.OfType<DotnetProjectResource>().Single(resource => resource.Name == WorkerName))
            .WithEnvironment("ConsigneeNotification__Mail__PickupDirectory", _mailFolder)
            .WithEnvironment("ConsigneeNotification__PartnerPortalBaseAddress", TrackingBaseAddress)
            .WithEnvironment("ConsigneeNotification__Retry__Interval", "00:00:00.200")
            .WithEnvironment("ConsigneeNotification__Retry__MaximumInterval", "00:00:00.800")
            // Aspire sets the formatter under this exact name, and environment names are case-sensitive on Linux.
            .WithEnvironment("LOGGING__CONSOLE__FORMATTERNAME", "json")
            .WithEnvironment("Logging__Console__FormatterOptions__IncludeScopes", "true")
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
    }

    private void WatchWorkerLogs()
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var loggers = App.Services.GetRequiredService<ResourceLoggerService>();
        var worker = model.Resources.Single(resource => resource.Name == WorkerName);
        _ = Task.Run(async () =>
        {
            await foreach (var batch in loggers.WatchAsync(worker))
            {
                foreach (var line in batch)
                {
                    if (TmsLog.TryParse(line.Content) is { } log)
                    {
                        _workerLogs.Enqueue(log);
                    }
                }
            }
        });
    }

    /// <summary>Every log of the worker and of the TMS API, to search for what must never be logged.</summary>
    public IEnumerable<TmsLog> AllLogs => [.. WorkerLogs, .. TmsLogs];

    /// <summary>The emails the worker has written, each as the text of its .eml file, soft line breaks and escapes undone so that a link or an address reads as the Consignee sees it.</summary>
    public IReadOnlyList<string> Emails() =>
        [.. Directory.EnumerateFiles(_mailFolder, "*.eml").Select(ReadEmail).OfType<string>()];

    /// <summary>The emails written to the address.</summary>
    public IReadOnlyList<string> EmailsTo(string address) =>
        [.. Emails().Where(email => email.Contains(address, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Waits for the one email to the address, which carries a link, and returns its text; fails the test when none comes.</summary>
    public async Task<string> EmailToAsync(string address, CancellationToken cancellationToken) =>
        await WaitForAsync(() => Task.FromResult(EmailsTo(address).FirstOrDefault(email => email.Contains("/track/", StringComparison.Ordinal))), $"an email to {address}", cancellationToken);

    /// <summary>Waits for the worker to log, in the run of the correlation ID, a message that contains the text; fails the test with the worker's end of the log when it never does.</summary>
    public async Task<TmsLog> WorkerLogAsync(string correlationId, string message, CancellationToken cancellationToken) =>
        await WaitForAsync(
            () => Task.FromResult(WorkerLogs.FirstOrDefault(log => log["correlationId"] == correlationId && log.Text.Contains(message, StringComparison.Ordinal))),
            $"the worker to log \"{message}\" for {correlationId}",
            cancellationToken);

    /// <summary>What the worker logged in the run of the correlation ID.</summary>
    public IReadOnlyList<TmsLog> WorkerLogsOf(string correlationId) => [.. WorkerLogs.Where(log => log["correlationId"] == correlationId)];

    /// <summary>
    /// Makes the mail server refuse to take an email, as a mail server that is down does: the folder the worker writes to becomes a file, so it can't send, and every try fails until the returned
    /// handle is disposed, which gives the folder back with the emails it held: the tests that ran before sent emails a later test counts, so breaking the mailbox must not lose them.
    /// </summary>
    public IDisposable BreakMailbox()
    {
        var kept = Directory.EnumerateFiles(_mailFolder).ToDictionary(file => Path.GetRelativePath(_mailFolder, file), File.ReadAllBytes);
        Directory.Delete(_mailFolder, recursive: true);
        File.WriteAllText(_mailFolder, string.Empty);
        return new MailboxRepair(_mailFolder, kept);
    }

    /// <summary>Sends the worker a request for the Transport Order as the TMS's outbox would, to show what it does with one the TMS wouldn't have asked for.</summary>
    public async Task SendRequestAsync(string transportOrderNumber, string correlationId, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["eventId"] = Guid.CreateVersion7().ToString(),
            ["eventType"] = "ConsigneeNotificationRequested",
            ["eventVersion"] = 1,
            ["occurredAt"] = DateTimeOffset.UtcNow,
            ["correlationId"] = correlationId,
            ["transportOrderNumber"] = transportOrderNumber,
            ["shipmentNumber"] = "SH-0000001",
            ["reason"] = "Collected",
        };
        await using var sender = _bus!.CreateSender(ShipmentEventsTopic);
        var message = new ServiceBusMessage(BinaryData.FromString(body.ToJsonString()))
        {
            MessageId = body["eventId"]!.GetValue<string>(),
            Subject = "ConsigneeNotificationRequested",
            CorrelationId = correlationId,
            ContentType = "application/json",
        };
        message.ApplicationProperties["eventType"] = "ConsigneeNotificationRequested";
        message.ApplicationProperties["correlationId"] = correlationId;
        await sender.SendMessageAsync(message, cancellationToken);
    }

    /// <summary>What the Consignee notification subscription holds as dead-lettered.</summary>
    public async Task<int> DeadLetteredAsync(CancellationToken cancellationToken)
    {
        await using var receiver = _bus!.CreateReceiver(ShipmentEventsTopic, Subscription, new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        return (await receiver.PeekMessagesAsync(500, cancellationToken: cancellationToken)).Count;
    }

    /// <summary>Gives the worker time to do what it would, so an email or a failure it should not have made shows. A test uses it only after a positive control has proved the path works.</summary>
    public static Task PauseAsync(CancellationToken cancellationToken) => Task.Delay(Pause, cancellationToken);

    /// <summary>Waits until the probe finds something, polling; fails the test with what it waited for when it doesn't.</summary>
    public async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, string what, CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WaitTimeout);
        try
        {
            while (true)
            {
                if (await probe() is { } found)
                {
                    return found;
                }

                await Task.Delay(PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var tail = string.Join(Environment.NewLine, WorkerLogs.TakeLast(15).Select(log => log.Text.Replace('\n', ' ')));
            throw new TimeoutException($"Waited {WaitTimeout.TotalSeconds:0} s for {what}.{Environment.NewLine}The worker's last logs:{Environment.NewLine}{tail}");
        }
    }

    // The worker sends through SmtpClient's pickup folder, which picks the body's encoding by its content: 7bit, quoted-printable (a soft line break is "=" at the end of a line, and "=XX" is a byte)
    // or base64. The tests read the body as the Consignee's mail program shows it, with the headers kept as they are.
    private static string? ReadEmail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var raw = reader.ReadToEnd();
            var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal) is var crlf and >= 0 ? (crlf, 4) : (raw.IndexOf("\n\n", StringComparison.Ordinal), 2);
            if (split.Item1 < 0)
            {
                return raw;
            }

            var headers = raw[..split.Item1];
            var body = raw[(split.Item1 + split.Item2)..];
            if (Regex.IsMatch(headers, @"^Content-Transfer-Encoding:\s*base64", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1)))
            {
                body = Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c)))));
            }
            else if (Regex.IsMatch(headers, @"^Content-Transfer-Encoding:\s*quoted-printable", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1)))
            {
                body = Regex.Replace(body, @"=\r?\n", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1));
                body = Regex.Replace(body, "=([0-9A-F]{2})", match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString(), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }

            return headers + "\r\n\r\n" + body;
        }
        catch (Exception exception) when (exception is IOException or FormatException)
        {
            // Being written (a body cut short is not yet valid): the next poll reads it whole.
            return null;
        }
        catch (IOException)
        {
            // Being written: the next poll reads it whole.
            return null;
        }
    }

    private sealed class MailboxRepair(string folder, Dictionary<string, byte[]> kept) : IDisposable
    {
        public void Dispose()
        {
            File.Delete(folder);
            Directory.CreateDirectory(folder);
            foreach (var (name, content) in kept)
            {
                File.WriteAllBytes(Path.Combine(folder, name), content);
            }
        }
    }
}
