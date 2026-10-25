namespace Norvia.Agents.Runner;

internal static class JsonReplies
{
    // Models often wrap JSON in a code fence.
    public static string WithoutFence(string reply)
    {
        var text = reply.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var start = text.IndexOf('\n', StringComparison.Ordinal);
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return start >= 0 && end > start ? text[(start + 1)..end].Trim() : text;
    }
}
