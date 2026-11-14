using System.Net;
using System.Text;

namespace Norvia.Erp.Web.UnitTests.ErpApi;

/// <summary>Stands in for the ERP API: answers with what the test sets, and keeps the request it got.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.OK);

    public HttpRequestMessage? Request { get; private set; }

    /// <summary>The body of the request it got, read before the caller disposes the request.</summary>
    public string? RequestBody { get; private set; }

    public void Answer(HttpStatusCode status, string body, string mediaType = "application/json") =>
        _answer = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _answer();
    }
}
