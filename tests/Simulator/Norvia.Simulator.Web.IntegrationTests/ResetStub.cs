using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// Stands in for the TMS API's reset door (ADR 0039) where a test needs to see or steer what the Simulator asks of it: it records every call, says its reset is
/// switched on, and either takes the reset or refuses it with a reason.
/// </summary>
public sealed class ResetStub : IDisposable
{
    public const string RefusalDetail = "A Service Bus subscription filled while the reset ran.";

    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly bool _refusing;

    private ResetStub(int port, bool refusing)
    {
        _refusing = refusing;
        Address = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Address.ToString());
    }

    public Uri Address { get; }

    /// <summary>The methods of the calls the Simulator has made so far, in order.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>A TMS API that would take a reset and answers that it did, deleting nothing.</summary>
    public static ResetStub Accepting() => Start(refusing: false);

    /// <summary>A TMS API that would take a reset, and refuses it with <see cref="RefusalDetail"/>.</summary>
    public static ResetStub Refusing() => Start(refusing: true);

    public void Dispose() => _listener.Close();

    private static ResetStub Start(bool refusing)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var stub = new ResetStub(((IPEndPoint)probe.LocalEndpoint).Port, refusing);
        probe.Stop();
        stub._listener.Start();
        _ = stub.ServeAsync();
        return stub;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _calls.Enqueue(context.Request.HttpMethod);
            var (status, type, body) = context.Request.HttpMethod switch
            {
                "GET" => (200, "application/json", """{"enabled":true,"busySubscription":null}"""),
                _ when _refusing => (409, "application/problem+json", $$"""{"title":"The reset was refused.","status":409,"detail":"{{RefusalDetail}}"}"""),
                _ => (200, "application/json", """{"deleted":[]}"""),
            };
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = type;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
