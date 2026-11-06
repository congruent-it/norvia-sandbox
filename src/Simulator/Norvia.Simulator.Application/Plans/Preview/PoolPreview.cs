namespace Norvia.Simulator.Application.Plans.Preview;

/// <summary>
/// One pool of Carrier texts as the preview shows it: which profile, kind of text and language, where its texts came from (<c>built-in</c>
/// or <c>agent, run N</c>), how many there are, a sample of them, and the Carriers that write in it.
/// </summary>
public sealed record PoolPreview(string ProfileId, string Category, string Language, string Source, int Count, IReadOnlyList<string> Samples, IReadOnlyList<string> CarrierIds);
