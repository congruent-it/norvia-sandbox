using System.Text.RegularExpressions;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Agents.Planner;

/// <summary>
/// Asks the Planner notification agent for a draft. Its two tools are the template for the Exception's type and the Shipper's view of the
/// Transport Order (ADR 0011), so the draft cannot name the Carrier or an internal remark or Exception: nothing it can call returns one.
/// The tools are the boundary; this handler is a second check on the finished draft, so a model that names a Carrier, the Exception or a
/// price anyway never reaches the Planner's box. The draft arrives as the Suggestion's cause; the TMS neither sends nor stores it.
/// </summary>
public sealed partial class DraftShipperNotificationHandler(IReadRepository<ExceptionCase> repository, IAgentClient agent, ICarrierDirectory carriers)
    : IQueryHandler<DraftShipperNotificationQuery, Result<Suggestion>>
{
    public const string AgentName = "planner-notification";

    private const int ShortestCarrierName = 3;

    public async ValueTask<Result<Suggestion>> Handle(DraftShipperNotificationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Only an open Exception can be notified about, so only an open one gets a draft.
        if (!ExceptionNumber.TryParse(query.ExceptionNumber, out var number)
            || await repository.FirstOrDefaultAsync(new ExceptionByNumberSpec(number), cancellationToken) is not { Status: ExceptionStatus.Open } exception)
        {
            return Result.NotFound();
        }

        // The Exception's own correlation ID, so the Agent's tool calls join its Message trail.
        var answer = await agent.AskAsync(
            new AgentRequest(AgentName, PlannerQuestions.DraftShipperNotification(number.Value, exception.TransportOrderNumber.Value), exception.CorrelationId.Value),
            cancellationToken);
        return answer.IsSuccess && Leaks(answer.Value.Cause, number)
            ? Result<Suggestion>.Unavailable(AgentFailureCodes.DraftNotAllowed)
            : answer;
    }

    // What the Shipper must not be told: the Exception, a Carrier by name, or a price.
    private bool Leaks(string draft, ExceptionNumber number) =>
        draft.Contains(number.Value, StringComparison.OrdinalIgnoreCase)
        || Money().IsMatch(draft)
        || carriers.ListActive().Any(carrier => carrier.Name.Length >= ShortestCarrierName && draft.Contains(carrier.Name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"[€$£]|\b(?:EUR|USD|GBP)\b|\b\d+(?:[.,]\d+)?\s?(?:euros?|dollars?|pounds?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Money();
}
