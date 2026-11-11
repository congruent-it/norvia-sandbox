using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Reads where a Transport Order waiting for planning can go (P3, ADR 0040): whether Own fleet fits and which Carriers can carry it at what
/// Carrier cost. The TMS computes, the Planner agent chooses (#372): the rules are the ones P3, P9 and P10 already run.
/// </summary>
/// <param name="AlreadyAssumed">
/// The what-if tally (#445): the numbers of the Transport Orders a plan has suggested for Own fleet so far, so Own fleet's load check counts
/// them too. Nothing is assigned. An order that is unknown, is not waiting for planning, is this order itself or collects on another day adds nothing.
/// </param>
public sealed record GetAssignmentOptionsQuery(string TransportOrderNumber, IReadOnlyList<string>? AlreadyAssumed = null) : IQuery<Result<AssignmentOptions>>;
