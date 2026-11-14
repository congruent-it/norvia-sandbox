using Ardalis.Specification;

namespace Norvia.Tms.Infrastructure.Data;

/// <summary>
/// Reads through a specification without waiting on a task, for the directories whose ports are synchronous (rating and planning read
/// the TMS's master data in the middle of a calculation). It never leaves the Infrastructure project, so no <c>DbContext</c> or query does.
/// </summary>
internal interface ISyncReadRepository<T>
    where T : class
{
    T? FirstOrDefault(ISpecification<T> specification);

    IReadOnlyList<T> List(ISpecification<T> specification);
}
