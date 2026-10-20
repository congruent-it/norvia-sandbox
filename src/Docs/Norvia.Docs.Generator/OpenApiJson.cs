using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Docs.Generator;

/// <summary>
/// Writes an OpenAPI document the same way every time (indented, LF line ends, one final newline), so a regeneration differs from the committed file only when the API changed.
/// What hosting the API in process leaks into the document is put right: the title names the API, not the process that hosted it, and the test server's address is no server.
/// </summary>
internal static class OpenApiJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Normalize(string json, string title, string version)
    {
        var document = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("The OpenAPI document is empty.");
        document.Remove("servers");
        if (document["info"] is JsonObject info)
        {
            info["title"] = $"{title} | {version}";
        }

        return document.ToJsonString(Options) + "\n";
    }
}
