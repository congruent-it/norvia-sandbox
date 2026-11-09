using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Norvia.Integration.BookingDraft;

/// <summary>The <c>booking-draft</c> contract, checked in process: the same schema the Partner API publishes (contracts/partner-api).</summary>
public static class BookingDraftSchema
{
    private static readonly Lazy<JsonSchema> Draft = new(() =>
    {
        var registry = new SchemaRegistry();
        var options = new BuildOptions { SchemaRegistry = registry };
        JsonSchema.FromText(Read("booking.v1.schema.json"), options);
        return JsonSchema.FromText(Read("booking-draft.v1.schema.json"), options);
    });

    /// <summary>What breaks the schema, or nothing when the document is a valid draft.</summary>
    public static IReadOnlyList<string> Errors(JsonNode? document)
    {
        var results = Draft.Value.Evaluate(
            JsonSerializer.SerializeToElement(document),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        return results.IsValid
            ? []
            : [.. results.Details?.Where(detail => detail.Errors is not null).SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")) ?? []];
    }

    private static string Read(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"contracts/{file}")
            ?? throw new InvalidOperationException($"{file} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
