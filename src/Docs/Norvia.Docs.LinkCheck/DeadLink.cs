namespace Norvia.Docs.LinkCheck;

/// <summary>A link whose target does not exist. Prints as <c>file:line -> target</c>, the target as written.</summary>
public sealed record DeadLink(string File, int Line, string Target)
{
    public override string ToString() => $"{File}:{Line} -> {Target}";
}
