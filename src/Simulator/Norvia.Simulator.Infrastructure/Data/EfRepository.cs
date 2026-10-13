using Ardalis.SharedKernel;
using Ardalis.Specification.EntityFrameworkCore;

namespace Norvia.Simulator.Infrastructure.Data;

internal sealed class EfRepository<T>(SimulatorDbContext dbContext) : RepositoryBase<T>(dbContext), IRepository<T>, IReadRepository<T>
    where T : class, IAggregateRoot;
