namespace Norvia.HealthCheck.Application.Checks;

/// <summary>
/// What a <see cref="Check"/> found wrong. Its severity, rule, as-found toggle and fix are the Check's own, copied here, so a Finding
/// says everything an operator needs and nothing a model wrote.
/// </summary>
/// <param name="Id">The Finding's ID, which the Agent cites: <c>F-</c> and the Check's ID.</param>
/// <param name="CheckId">The Check that found it.</param>
/// <param name="Title">One line: what is wrong.</param>
/// <param name="Rule">The rule of the Check that the estate breaks.</param>
/// <param name="Evidence">The resources concerned, masked.</param>
/// <param name="AsFoundToggle">The as-found toggle that makes the estate this way (<c>docs/operations/as-found.md</c>).</param>
/// <param name="Fix">The Bicep parameter or setting that puts it right.</param>
public sealed record Finding(
    string Id,
    string CheckId,
    string Title,
    string Rule,
    Severity Severity,
    IReadOnlyList<Evidence> Evidence,
    string AsFoundToggle,
    string Fix);

/// <summary>One resource a Finding concerns, and what is wrong with it. Never a secret's value: a secret is reported by where it is.</summary>
public sealed record Evidence(string Subject, string Detail);
