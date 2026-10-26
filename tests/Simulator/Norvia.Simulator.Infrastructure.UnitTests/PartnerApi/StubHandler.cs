using System.Net;
using System.Text;

namespace Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

/// <summary>Stands in for the Norvia Partner API: answers with what the test sets, and keeps the request it got.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.OK);

    public HttpRequestMessage? Request { get; private set; }

    /// <summary>How many requests the stub has answered.</summary>
    public int Calls { get; private set; }

    public string? RequestBody { get; private set; }

    /// <summary>The request's Content-Length as the Simulator sent it: without one the body goes out in chunks.</summary>
    public long? RequestContentLength { get; private set; }

    public void Answer(HttpStatusCode status, string body, string? correlationId = null) => _answer = () =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (correlationId is not null)
        {
            response.Headers.Add("X-Correlation-ID", correlationId);
        }

        return response;
    };

    /// <summary>Answers the first request as <paramref name="first"/> and every one after it as <paramref name="then"/>.</summary>
    public void AnswerFirst(HttpStatusCode first, string firstBody, HttpStatusCode then, string thenBody)
    {
        Answer(then, thenBody);
        var rest = _answer;
        Answer(first, firstBody);
        var opening = _answer;
        _answer = () => Calls <= 1 ? opening() : rest();
    }

    public void Fail(Exception exception) => _answer = () => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        Calls++;
        RequestContentLength = request.Content?.Headers.ContentLength;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _answer();
    }
}
