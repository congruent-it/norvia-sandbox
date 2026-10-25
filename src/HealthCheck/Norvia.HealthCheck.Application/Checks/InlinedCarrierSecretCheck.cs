using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>A Carrier endpoint secret is read from Key Vault, never kept in a Logic App.</summary>
public sealed class InlinedCarrierSecretCheck : Check
{
    public const string CheckId = "SECRET-INLINED";

    public override string Id => CheckId;

    public override string Rule => "A Carrier endpoint secret is read from Key Vault, never kept in a Logic App.";

    public override string Title => "A Carrier endpoint secret is inlined in a Logic App";

    public override Severity Severity => Severity.Critical;

    public override string AsFoundToggle => "carrierSecretInlined";

    public override string Fix =>
        "Set carrierSecretInlined to false and provision the estate, then rotate the Carrier endpoint secret, which has been in clear text. The Logic App reads it from Key Vault (KEY_VAULT_URI).";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return
        [
            .. (estate.InlinedCarrierSecrets ?? [])
                .OrderBy(location => location, StringComparer.Ordinal)
                .Select(location => new Evidence(location, $"A Carrier endpoint secret sits in clear text at {location}. Its value is not reported.")),
        ];
    }
}
