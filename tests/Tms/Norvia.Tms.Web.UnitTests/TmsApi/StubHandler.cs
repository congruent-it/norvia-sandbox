using System.Net;
using System.Text;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

/// <summary>Stands in for the TMS API: answers with what the test sets, and keeps the request it got.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.OK);

    public HttpRequestMessage? Request { get; private set; }

    public void Answer(HttpStatusCode status, string body, string mediaType = "application/json") =>
        _answer = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        return Task.FromResult(_answer());
    }
}
