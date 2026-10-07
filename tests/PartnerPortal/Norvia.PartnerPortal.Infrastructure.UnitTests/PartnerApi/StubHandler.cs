using System.Net;
using System.Text;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

/// <summary>Stands in for the Norvia Partner API: answers with what the test sets, and keeps the request it got.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.OK);

    public HttpRequestMessage? Request { get; private set; }

    public string? RequestBody { get; private set; }

    /// <summary>
    /// The request's Content-Length as the portal sent it, read before the body is: without one the body goes out in
    /// chunks.
    /// </summary>
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

    public void AnswerFile(string contentType, byte[] content, string fileName, string correlationId) => _answer = () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = fileName };
        response.Headers.Add("X-Correlation-ID", correlationId);
        return response;
    };

    public void Fail(Exception exception) => _answer = () => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        RequestContentLength = request.Content?.Headers.ContentLength;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _answer();
    }
}
