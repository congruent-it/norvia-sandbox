using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Norvia.Simulator.Web.UiTests;

/// <summary>
/// Stands in for the TMS API's reset door (ADR 0039) where the real one is switched on: two of them, one that says its reset is switched off and one that
/// takes the check and then refuses the reset with a reason. The Simulator reaches them through its TMS API address.
/// </summary>
public sealed class ResetStub : IDisposable
{
    public const string RefusalDetail = "A Service Bus subscription filled while the reset ran.";

    private readonly HttpListener _listener = new();
    private readonly Uri _root;

    private ResetStub(int port)
    {
        _root = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(_root.ToString());
    }

    /// <summary>A TMS API whose reset is switched off.</summary>
    public Uri SwitchedOff => new(_root, "off/");

    /// <summary>A TMS API that would take a reset, and refuses it.</summary>
    public Uri Refusing => new(_root, "refusing/");

    public static ResetStub Start()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var stub = new ResetStub(((IPEndPoint)probe.LocalEndpoint).Port);
        probe.Stop();
        stub._listener.Start();
        _ = stub.ServeAsync();
        return stub;
    }

    public void Dispose() => _listener.Close();

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

            var refusing = context.Request.Url!.AbsolutePath.StartsWith("/refusing/", StringComparison.Ordinal);
            var (status, type, body) = context.Request.HttpMethod switch
            {
                "GET" => (200, "application/json", $$"""{"enabled":{{(refusing ? "true" : "false")}},"busySubscription":null}"""),
                _ => (409, "application/problem+json", $$"""{"title":"The reset was refused.","status":409,"detail":"{{RefusalDetail}}"}"""),
            };
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = type;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
