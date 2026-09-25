using System.Net;
using System.Text;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>Stands in for the TMS intake API: answers with what the test sets, and keeps every request it got.</summary>
public sealed class StubTms : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

    public void Answer(HttpStatusCode status, string body, string contentType = "application/json") =>
        _answer = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    public void Fail(Exception exception) => _answer = () => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
        return _answer();
    }
}
