namespace Norvia.Tms.Domain.Exceptions;

public readonly record struct ExceptionId(Guid Value)
{
    public static ExceptionId New() => new(Guid.CreateVersion7());
}
