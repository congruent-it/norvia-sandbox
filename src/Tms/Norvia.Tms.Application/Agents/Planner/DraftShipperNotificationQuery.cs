using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>Ask the Planner notification agent for an editable draft of a Notify Shipper message on an open Exception. A Suggestion: the TMS sends nothing until the Planner sends it through Notify Shipper (P7).</summary>
public sealed record DraftShipperNotificationQuery(string ExceptionNumber) : IQuery<Result<Suggestion>>;
