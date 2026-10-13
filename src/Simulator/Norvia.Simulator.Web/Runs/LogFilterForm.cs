namespace Norvia.Simulator.Web.Runs;

/// <summary>What the Run screen's filter form holds. Every field is optional, so there is nothing to validate.</summary>
public sealed class LogFilterForm
{
    public string Actor { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string Text { get; set; } = "";
}
