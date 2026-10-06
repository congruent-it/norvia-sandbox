using Ardalis.Specification;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The Planner's list of Open Exceptions (P7), oldest first.</summary>
public sealed class OpenExceptionsSpec : Specification<ExceptionCase>
{
    public OpenExceptionsSpec()
    {
        Query.Where(exception => exception.Status == ExceptionStatus.Open)
            .OrderBy(exception => exception.OpenedAt)
            .ThenBy(exception => exception.Number)
            .AsNoTracking();
    }
}
