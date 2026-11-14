using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The replica with an ID, to read.</summary>
public sealed class DriverCopyByIdSpec : Specification<DriverCopy>
{
    public DriverCopyByIdSpec(string id)
    {
        Query.AsNoTracking().Where(copy => copy.Id == id);
    }
}
