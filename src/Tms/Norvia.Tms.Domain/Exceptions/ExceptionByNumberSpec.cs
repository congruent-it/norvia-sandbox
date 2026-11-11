using Ardalis.Specification;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The Exception with the number, not tracked, for reading it, open or resolved.</summary>
public sealed class ExceptionByNumberSpec : SingleResultSpecification<ExceptionCase>
{
    public ExceptionByNumberSpec(ExceptionNumber number)
    {
        Number = number;
        Query.Where(exception => exception.Number == number).AsNoTracking();
    }

    public ExceptionNumber Number { get; }
}
