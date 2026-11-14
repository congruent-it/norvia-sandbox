using Ardalis.SharedKernel;
using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;

namespace Norvia.Tms.Infrastructure.Data;

internal sealed class EfRepository<T>(TmsDbContext dbContext) : RepositoryBase<T>(dbContext), IRepository<T>, IReadRepository<T>, ISyncReadRepository<T>
    where T : class, IAggregateRoot
{
    public T? FirstOrDefault(ISpecification<T> specification) => ApplySpecification(specification).FirstOrDefault();

    public IReadOnlyList<T> List(ISpecification<T> specification) => ApplySpecification(specification).ToList();
}
