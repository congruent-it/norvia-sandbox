using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.Trails.Search;

/// <summary>
/// The Message trails that carried a key: a correlation ID, Transport Order number, Shipment number, Carrier booking
/// number, Trip number or source message ID. <paramref name="Within"/> is how far back to look, the last 24 hours when
/// left out and at most the retention period.
/// </summary>
public sealed record SearchTrailsQuery(string Key, TimeSpan? Within = null) : IQuery<Result<TrailSearchResult>>;
