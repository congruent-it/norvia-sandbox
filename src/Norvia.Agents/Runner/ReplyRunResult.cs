namespace Norvia.Agents.Runner;

/// <summary>A finished run of an Agent whose reply the caller reads itself, without a code fence around it.</summary>
public sealed record ReplyRunResult(string RunId, string Reply);
