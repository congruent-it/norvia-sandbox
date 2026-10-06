using Ardalis.Specification;

namespace Norvia.Tms.Domain.Exceptions;

/// <summary>The Exception with the number, tracked, so resolving it saves its changes.</summary>
public sealed class ExceptionToResolveSpec : SingleResultSpecification<ExceptionCase>
{
    public ExceptionToResolveSpec(ExceptionNumber number)
    {
        Number = number;
        Query.Where(exception => exception.Number == number);
    }

    public ExceptionNumber Number { get; }
}
