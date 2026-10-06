using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.Exceptions;

internal sealed class SqlExceptionNumbers(TmsDbContext dbContext) : IExceptionNumbers
{
    public async Task<ExceptionNumber> NextAsync(CancellationToken cancellationToken) =>
        ExceptionNumber.FromSequence(await SqlSequence.NextAsync(dbContext, TmsDbContext.ExceptionNumberSequence, cancellationToken));
}
