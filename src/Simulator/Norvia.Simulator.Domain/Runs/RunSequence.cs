using Ardalis.SharedKernel;

namespace Norvia.Simulator.Domain.Runs;

/// <summary>
/// Hands out run numbers. Clearing the history keeps it, so a Shipper reference <c>SIM-&lt;run&gt;-&lt;n&gt;</c> is never used
/// twice: there is no wipe of the estate, and Norvia accepts each reference once per Shipper.
/// </summary>
public sealed class RunSequence : EntityBase<int>, IAggregateRoot
{
    private RunSequence()
    {
        Id = 1;
    }

    public int Last { get; private set; }

    /// <summary>The number the next run gets.</summary>
    public int Next => Last + 1;

    public static RunSequence Create() => new();

    public int Take() => ++Last;
}
