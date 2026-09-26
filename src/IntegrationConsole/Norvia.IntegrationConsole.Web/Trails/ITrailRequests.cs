using Ardalis.Result;
using Norvia.IntegrationConsole.Application.Trails.Open;
using Norvia.IntegrationConsole.Application.Trails.Search;

namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>What the Search and Message trail pages ask of the application.</summary>
public interface ITrailRequests
{
    Task<Result<TrailSearchResult>> SearchAsync(SearchTrailsQuery query, CancellationToken cancellationToken);

    Task<Result<MessageTrail>> OpenAsync(OpenTrailQuery query, CancellationToken cancellationToken);
}
