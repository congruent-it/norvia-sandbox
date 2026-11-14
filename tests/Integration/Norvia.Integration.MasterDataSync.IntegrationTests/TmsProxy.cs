using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>What the TMS does with a call the master-data sync worker makes while a test scripts it: answers a status of the test's own, or does not answer.</summary>
public abstract record Fault
{
    /// <summary>The call goes through to the TMS.</summary>
    public static Fault Forward { get; } = new ForwardFault();

    /// <summary>The TMS answers this status without having seen the call.</summary>
    public static Fault Answer(int status) => new AnswerFault(status);

    /// <summary>The connection is dropped with no answer, as a TMS that is down or too slow gives.</summary>
    public static Fault NoAnswer { get; } = new NoAnswerFault();

    internal sealed record ForwardFault : Fault;

    internal sealed record AnswerFault(int Status) : Fault;

    internal sealed record NoAnswerFault : Fault;
}

/// <summary>One call the worker made to the TMS, as the proxy saw it.</summary>
public sealed record TmsCall(string Method, string Path, string? CorrelationId, string? Token, Fault Fault);

/// <summary>
/// Stands between the master-data sync worker and the TMS API in the AppHost. A call goes through to the TMS unless a test has scripted a fault for its path,
/// so the worker's retries and dead letters can be shown against a TMS that fails on cue, while every other call is the real TMS's. It keeps the calls it saw.
/// </summary>
public sealed class TmsProxy : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Fault>> _scripts = new();
    private readonly ConcurrentQueue<TmsCall> _calls = new();
    private readonly HttpClient _tms = new(new SocketsHttpHandler { AllowAutoRedirect = false });
    private WebApplication? _app;
    private Uri? _target;

    /// <summary>Where the worker is told the TMS is.</summary>
    public Uri Address { get; private set; } = null!;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Run(HandleAsync);
        await _app.StartAsync();
        Address = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    /// <summary>Where the real TMS API is, once the AppHost has started it.</summary>
    public void ForwardTo(Uri tms) => _target = tms;

    /// <summary>The faults the next calls to the path meet, one each, in order; calls after them go through.</summary>
    public void Script(string path, params Fault[] faults)
    {
        var queue = _scripts.GetOrAdd(path, _ => new());
        foreach (var fault in faults)
        {
            queue.Enqueue(fault);
        }
    }

    /// <summary>How many calls the worker has made to the TMS in all; the tests run one at a time, so a test can tell whether the worker called it at all.</summary>
    public int CallCount => _calls.Count;

    /// <summary>The calls the worker made to the path.</summary>
    public IReadOnlyList<TmsCall> CallsTo(string path) => [.. _calls.Where(call => call.Path == path)];

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value!;
        var fault = _scripts.TryGetValue(path, out var queue) && queue.TryDequeue(out var scripted) ? scripted : Fault.Forward;
        _calls.Enqueue(new TmsCall(
            context.Request.Method, path, context.Request.Headers["X-Correlation-ID"].FirstOrDefault(), context.Request.Headers.Authorization.FirstOrDefault(), fault));

        switch (fault)
        {
            case Fault.NoAnswerFault:
                context.Abort();
                return;
            case Fault.AnswerFault answer:
                context.Response.StatusCode = answer.Status;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync($$"""{"title":"Scripted by the test.","status":{{answer.Status}},"code":"Scripted","detail":"The test scripted {{answer.Status}}."}""");
                return;
        }

        using var forwarded = new HttpRequestMessage(new HttpMethod(context.Request.Method), new Uri(_target!, path))
        {
            Content = new StreamContent(context.Request.Body),
        };
        forwarded.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        foreach (var name in new[] { "Authorization", "X-Correlation-ID" })
        {
            if (context.Request.Headers.TryGetValue(name, out var value))
            {
                forwarded.Headers.TryAddWithoutValidation(name, value.ToArray());
            }
        }

        using var answered = await _tms.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)answered.StatusCode;
        if (answered.Content.Headers.ContentType is { } contentType)
        {
            context.Response.ContentType = contentType.ToString();
        }

        await answered.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    public async ValueTask DisposeAsync()
    {
        _tms.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
