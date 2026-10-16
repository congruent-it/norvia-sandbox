namespace Norvia.Tms.Application.Reset;

/// <summary>What a reset deleted, by kind.</summary>
public sealed record ResetReport(IReadOnlyList<DeletedKind> Deleted);
