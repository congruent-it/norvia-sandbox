namespace Norvia.Tms.Infrastructure.Agents;

/// <summary>Makes the ID of an Agent run, which the run's span and its answer carry.</summary>
internal interface IRunIds
{
    string Next();
}

internal sealed class GuidRunIds : IRunIds
{
    public string Next() => Guid.NewGuid().ToString("N");
}
