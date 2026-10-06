using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions;

/// <summary>Hands out Exception numbers, each only once.</summary>
public interface IExceptionNumbers
{
    Task<ExceptionNumber> NextAsync(CancellationToken cancellationToken);
}
