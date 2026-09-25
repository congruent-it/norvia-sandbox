using Ardalis.SharedKernel;
using Ardalis.Specification.EntityFrameworkCore;

namespace Norvia.PartnerPortal.Infrastructure.Data;

internal sealed class EfRepository<T>(PartnerPortalDbContext dbContext) : RepositoryBase<T>(dbContext), IRepository<T>, IReadRepository<T>
    where T : class, IAggregateRoot;
