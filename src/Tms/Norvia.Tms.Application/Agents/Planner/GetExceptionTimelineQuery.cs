using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>Reads the timeline of one Exception for the Planner agent's tool <c>get_exception_timeline</c>: staff-side facts, never a Consignee's personal data or a file.</summary>
public sealed record GetExceptionTimelineQuery(string ExceptionNumber) : IQuery<Result<ExceptionTimelineForAgent>>;
