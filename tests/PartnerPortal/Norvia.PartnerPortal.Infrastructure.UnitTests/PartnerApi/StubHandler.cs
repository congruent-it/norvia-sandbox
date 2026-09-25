using System.Net;
using System.Text;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

/// <summary>Stands in for the Norvia Partner API: answers with what the test sets, and keeps the request it got.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.OK);

    public HttpRequestMessage? Request { get; private set; }

    public string? RequestBody { get; private set; }

    public void Answer(HttpStatusCode status, string body, string? correlationId = null) => _answer = () =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (correlationId is not null)
        {
            response.Headers.Add("X-Correlation-ID", correlationId);
        }

        return response;
    };

    public void Fail(Exception exception) => _answer = () => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _answer();
    }
}
