using Ardalis.SharedKernel;
using Ardalis.Specification.EntityFrameworkCore;

namespace Norvia.Tms.Infrastructure.Data;

public sealed class EfRepository<T>(TmsDbContext dbContext) : RepositoryBase<T>(dbContext), IRepository<T>, IReadRepository<T>
    where T : class, IAggregateRoot;
