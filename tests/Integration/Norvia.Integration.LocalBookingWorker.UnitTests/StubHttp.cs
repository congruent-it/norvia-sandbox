using System.Net;
using System.Text;

namespace Norvia.Integration.LocalBookingWorker.UnitTests;

/// <summary>Stands in for an HTTP endpoint: answers each request with the next of what the test queued, and keeps what it got.</summary>
public sealed class StubHttp(TimeProvider time) : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();
    private Func<HttpResponseMessage> _otherwise = () => new HttpResponseMessage(HttpStatusCode.OK);

    public List<Request> Requests { get; } = [];

    public StubHttp Then(HttpStatusCode status, string body = "")
    {
        _answers.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") });
        return this;
    }

    public StubHttp ThenFail(Exception exception)
    {
        _answers.Enqueue(() => throw exception);
        return this;
    }

    public StubHttp Otherwise(HttpStatusCode status, string body = "")
    {
        _otherwise = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new(
            request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => header.Value.First()),
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken), time.GetUtcNow()));
        return _answers.TryDequeue(out var answer) ? answer() : _otherwise();
    }

    public sealed record Request(Uri Uri, Dictionary<string, string> Headers, string Body, DateTimeOffset At);
}
