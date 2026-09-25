namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>A Persona's stable ID from the seed file, such as <c>brouwer-dranken</c>.</summary>
public sealed record PersonaId
{
    public PersonaId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
