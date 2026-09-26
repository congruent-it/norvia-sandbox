using Ardalis.Result;
using Norvia.IntegrationConsole.Application.Trails.Search;

namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>What the Search page asks of the application.</summary>
public interface ITrailSearch
{
    Task<Result<TrailSearchResult>> SearchAsync(SearchTrailsQuery query, CancellationToken cancellationToken);
}
