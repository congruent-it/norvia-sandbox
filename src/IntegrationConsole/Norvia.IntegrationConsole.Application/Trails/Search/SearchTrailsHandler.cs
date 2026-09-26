using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Options;

namespace Norvia.IntegrationConsole.Application.Trails.Search;

public sealed class SearchTrailsHandler(ITrailSource source, TimeProvider time, IOptions<IntegrationRetention> retention)
    : IQueryHandler<SearchTrailsQuery, Result<TrailSearchResult>>
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);

    public async ValueTask<Result<TrailSearchResult>> Handle(SearchTrailsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var key = query.Key?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return Result.Invalid(new ValidationError(nameof(query.Key), "Type a correlation ID or a business key to search for."));
        }

        var range = query.Within ?? DefaultRange;
        if (range <= TimeSpan.Zero)
        {
            return Result.Invalid(new ValidationError(nameof(query.Within), "Choose a time range to search in."));
        }

        var to = time.GetUtcNow();
        var from = to - Capped(range);
        var found = await source.SearchAsync(new TrailSearch(key, from, to), cancellationToken);

        // What went wrong before what went right.
        var trails = found.Select(TrailSummary.From)
            .OrderByDescending(trail => trail.HasFailure)
            .ThenByDescending(trail => trail.LastSeen)
            .ToList();

        return new TrailSearchResult(from, to, trails);
    }

    // Nothing older than the retention period is kept, so looking further back only makes the search slower.
    private TimeSpan Capped(TimeSpan range) => range > retention.Value.Period ? retention.Value.Period : range;
}
