namespace Norvia.Agents.Runner;

/// <summary>A finished run of an Agent that generates texts.</summary>
public sealed record TextsRunResult(string RunId, IReadOnlyList<string> Texts);
