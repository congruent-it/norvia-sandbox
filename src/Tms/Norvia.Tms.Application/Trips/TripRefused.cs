using Ardalis.Result;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// A Planner's action on a Trip that the TMS refused, as a result: Invalid, with one error per broken rule, each carrying
/// the rule's message and its stable code (a <see cref="TripAvailabilityCode"/> or a <see cref="TripRefusalCode"/>).
/// </summary>
public static class TripRefused
{
    public static Result<TResult> Because<TResult>(TripRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result<TResult>.Invalid(new ValidationError("Trip", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }

    public static Result Because(TripRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Result.Invalid(new ValidationError("Trip", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error));
    }

    public static Result<TResult> Because<TResult>(TripPlanningCheck check) =>
        Result<TResult>.Invalid(ErrorsOf(check));

    public static Result Because(TripPlanningCheck check) =>
        Result.Invalid(ErrorsOf(check));

    public static Result<TResult> Because<TResult>(IReadOnlyList<TripAvailabilityFailure> failures) =>
        Result<TResult>.Invalid(ErrorsOf(failures));

    public static Result Because(IReadOnlyList<TripAvailabilityFailure> failures) =>
        Result.Invalid(ErrorsOf(failures));

    private static List<ValidationError> ErrorsOf(TripPlanningCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return
        [
            .. ErrorsOf(check.Failures),
            .. check.Refusals.Select(refusal => new ValidationError("Trip", refusal.Message, refusal.Code.ToString(), ValidationSeverity.Error)),
        ];
    }

    private static List<ValidationError> ErrorsOf(IReadOnlyList<TripAvailabilityFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return [.. failures.Select(failure => new ValidationError("Trip", failure.Message, failure.Code.ToString(), ValidationSeverity.Error))];
    }
}
