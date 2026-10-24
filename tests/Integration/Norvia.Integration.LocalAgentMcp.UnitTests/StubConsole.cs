using System.Net;
using System.Text;

namespace Norvia.Integration.LocalAgentMcp.UnitTests;

/// <summary>Stands in for the console's agent API: answers with what the test sets, and keeps every request it got.</summary>
public sealed class StubConsole : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    public List<HttpRequestMessage> Calls { get; } = [];

    public void Answer(HttpStatusCode status, string body) =>
        _answer = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public void Fail(Exception exception) => _answer = () => throw exception;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(_answer());
    }
}
