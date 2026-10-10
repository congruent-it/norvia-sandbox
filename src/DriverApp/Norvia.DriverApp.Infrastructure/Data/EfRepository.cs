using Ardalis.SharedKernel;
using Ardalis.Specification.EntityFrameworkCore;

namespace Norvia.DriverApp.Infrastructure.Data;

internal sealed class EfRepository<T>(DriverAppDbContext dbContext) : RepositoryBase<T>(dbContext), IRepository<T>, IReadRepository<T>
    where T : class, IAggregateRoot;
