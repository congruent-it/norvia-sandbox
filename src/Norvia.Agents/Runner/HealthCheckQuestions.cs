namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed question an operator puts to the Estate health-check agent. Nothing free-form reaches the model (ADR 0040). The scripted
/// model reads the same text back to tell it from another question.
/// </summary>
public static class HealthCheckQuestions
{
    public const string Narrate =
        "Run the estate's health check and narrate its Findings: put them in order, explain in plain language what each one means for the business, and cite the Finding by its ID.";

    public static bool IsNarrate(string question) => string.Equals(question, Narrate, StringComparison.Ordinal);
}
