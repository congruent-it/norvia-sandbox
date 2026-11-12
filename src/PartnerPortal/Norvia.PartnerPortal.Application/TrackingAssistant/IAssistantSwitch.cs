namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>Whether the assistant is on (<c>Agents:Enabled</c>, off by default as ADR 0040 has it). The tracking screen shows its panel only then.</summary>
public interface IAssistantSwitch
{
    bool Enabled { get; }
}
