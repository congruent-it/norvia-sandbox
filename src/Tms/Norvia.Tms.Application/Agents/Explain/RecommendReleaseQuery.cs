using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Explain;

/// <summary>Ask the Customer service agent whether a Delivered Transport Order should be released without a Proof of delivery: Release, Wait or Needs a person. It never executes.</summary>
public sealed record RecommendReleaseQuery(string TransportOrderNumber) : IQuery<Result<Suggestion>>;
