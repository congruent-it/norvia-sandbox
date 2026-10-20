using Microsoft.EntityFrameworkCore.Metadata;

namespace Norvia.Docs.Generator;

/// <summary>
/// An application with a page under docs/applications. <paramref name="Model"/> is its EF Core model, when it has a database;
/// <paramref name="OpenApi"/> reads the OpenAPI documents it publishes by version, when it has an API that does.
/// </summary>
internal sealed record DocumentedApplication(
    string Slug,
    Func<IModel>? Model = null,
    Func<Task<IReadOnlyDictionary<string, string>>>? OpenApi = null)
{
    public string PagePath => $"docs/applications/{Slug}.md";

    public string DescriptionsPath => $"docs/applications/descriptions/{Slug}.json";

    public string OpenApiPath(string version) => $"docs/applications/openapi/{Slug}.{version}.json";
}
