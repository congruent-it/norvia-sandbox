namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>
/// One pool of Carrier or Driver texts as the preview shows it: which profile, kind of text and language, where its texts came from (<c>built-in</c>
/// or <c>agent, run N</c>), how many there are, a sample of them, and the Carriers (or, for a pool of Driver texts, the Drivers) that write in it.
/// </summary>
public sealed record PoolPreview(string ProfileId, string Category, string Language, string Source, int Count, IReadOnlyList<string> Samples, IReadOnlyList<string> CarrierIds, IReadOnlyList<string>? DriverIds = null)
{
    /// <summary>True for a pool of Driver texts.</summary>
    public bool IsDriverText => DriverIds is not null;
}
