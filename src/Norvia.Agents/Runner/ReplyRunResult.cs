namespace Norvia.Agents.Runner;

/// <summary>A finished run of an Agent whose reply the caller reads itself, without a code fence around it, and the tokens the model reported for it (0 when it reported none).</summary>
public sealed record ReplyRunResult(string RunId, string Reply, int Tokens = 0);
