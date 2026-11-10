using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Reads where a Transport Order waiting for planning can go (P3, ADR 0040): whether Own fleet fits and which Carriers can carry it at what
/// Carrier cost. The TMS computes, the Planner agent chooses (#372): the rules are the ones P3, P9 and P10 already run.
/// </summary>
public sealed record GetAssignmentOptionsQuery(string TransportOrderNumber) : IQuery<Result<AssignmentOptions>>;
