namespace Norvia.Agents.Runner;

/// <summary>
/// The fixed question an operator puts to the FinOps agent. Nothing free-form reaches the model (ADR 0040). The scripted model reads the
/// same text back to tell it from another question.
/// </summary>
public static class FinOpsQuestions
{
    /// <summary>The FinOps agent's definition (agents/finops.agent.yaml). Its narration is checked for figures no tool result holds.</summary>
    public const string AgentName = "finops";

    public const string Narrate =
        "Explain why the estate's spend changed: read the cost view and the rising-spend Findings, name the resource that drives a rise and the as-found toggle that causes it, and cite the Finding by its ID.";

    public static bool IsNarrate(string question) => string.Equals(question, Narrate, StringComparison.Ordinal);
}
