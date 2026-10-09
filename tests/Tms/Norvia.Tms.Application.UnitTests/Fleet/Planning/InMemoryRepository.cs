using Ardalis.SharedKernel;
using Ardalis.Specification;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

/// <summary>A repository over a list, answering specifications by evaluating them in memory.</summary>
internal static class InMemoryRepository
{
    public static IRepository<T> Over<T>(List<T> stored)
        where T : class, IAggregateRoot
    {
        var repository = Substitute.For<IRepository<T>, IReadRepository<T>>();
        repository.ListAsync(Arg.Any<ISpecification<T>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<T>>().Evaluate(stored).ToList());
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => stored.ToList());
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<T>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ISpecification<T>>().Evaluate(stored).FirstOrDefault());
        repository.AddAsync(Arg.Any<T>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                stored.Add(call.Arg<T>());
                return call.Arg<T>();
            });
        repository.UpdateAsync(Arg.Any<T>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var updated = call.Arg<T>();
                stored.RemoveAll(item => item.Equals(updated) || SameKey(item, updated));
                stored.Add(updated);
                return Task.FromResult(1);
            });
        repository.DeleteAsync(Arg.Any<T>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                stored.Remove(call.Arg<T>());
                return Task.FromResult(1);
            });
        return repository;
    }

    /// <summary>The same repository, as the read side a query handler takes.</summary>
    public static IReadRepository<T> AsRead<T>(this IRepository<T> repository)
        where T : class, IAggregateRoot => (IReadRepository<T>)repository;

    // Only Roster is updated in place, and a Roster is identified by its Driver.
    private static bool SameKey<T>(T left, T right) =>
        left is Norvia.Tms.Domain.Fleet.Roster a && right is Norvia.Tms.Domain.Fleet.Roster b && a.DriverId == b.DriverId;
}
