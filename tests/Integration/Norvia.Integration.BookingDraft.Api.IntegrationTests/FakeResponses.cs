using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.BookingDraft.Api.IntegrationTests;

/// <summary>The project's Responses endpoint: a POST is answered as the test says, a DELETE with the status the test sets.</summary>
internal sealed class FakeResponses : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private JsonObject[] _output = [];
    private string _status = "completed";
    private HttpStatusCode? _failure;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.OK;

    public bool Hang { get; set; }

    public void Answer(params JsonObject[] output) => _output = output;

    public void Answer(string status, params JsonObject[] output) => (_status, _output) = (status, output);

    public void Fail(HttpStatusCode status) => _failure = status;

    public static JsonObject Message(string text) => new()
    {
        ["type"] = "message",
        ["role"] = "assistant",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
        }

        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(DeleteStatus);
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (_failure is { } failure)
        {
            return new HttpResponseMessage(failure);
        }

        var response = new JsonObject
        {
            ["id"] = "resp-1",
            ["object"] = "response",
            ["status"] = _status,
            ["output"] = new JsonArray(_output.Select(item => (JsonNode?)item.DeepClone()).ToArray()),
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
