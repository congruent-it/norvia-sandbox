using Ardalis.SharedKernel;
using Ardalis.Specification.EntityFrameworkCore;

namespace Norvia.Erp.Infrastructure.Data;

internal sealed class EfRepository<T>(ErpDbContext dbContext) : RepositoryBase<T>(dbContext), IRepository<T>, IReadRepository<T>
    where T : class, IAggregateRoot;
