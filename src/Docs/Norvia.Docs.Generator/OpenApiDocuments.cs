using System.Diagnostics.CodeAnalysis;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Norvia.Docs.Generator;

/// <summary>
/// Reads the OpenAPI documents an API publishes (one per version) by hosting it in process. No hosted service runs and no option is validated, so the API
/// needs no database, queue or token issuer: all it does is describe its endpoints.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Hosts a whole API; the drift tests run it against the committed documents.")]
internal static class OpenApiDocuments
{
    /// <typeparam name="TEntryPoint">Any type in the API's assembly: the host is started from that assembly's entry point.</typeparam>
    /// <param name="projectPath">The API's project directory, relative to the repository root, which is its content root.</param>
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync<TEntryPoint>(string projectPath)
        where TEntryPoint : class
    {
        await using var factory = new WebApplicationFactory<TEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(Path.Combine(RepositoryRoot.Find(), projectPath));

            // The APIs refuse to build their bearer options without a token issuer to trust, though nobody signs in to read a document.
            builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", "documentation");
            builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", "documentation");
            builder.UseSetting("Authentication:Schemes:Bearer:Authority", "https://documentation.invalid");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IStartupValidator>();
            });
        });
        using var client = factory.CreateClient();

        var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var version in factory.Services.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions)
        {
            documents[version.GroupName] = OpenApiJson.Normalize(await client.GetStringAsync($"/openapi/{version.GroupName}.json"), typeof(TEntryPoint).Assembly.GetName().Name!, version.GroupName!);
        }

        return documents;
    }
}
