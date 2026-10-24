using System.Text.Json.Nodes;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The presenter panel's fault that disables a Logic App runs as the Partner portal's managed identity, which may only start and stop that one
/// Logic App (docs/operations/as-found.md, docs/architecture/identity-and-access.md). The Bicep is evaluated locally with <c>bicep snapshot</c>;
/// nothing is deployed.
/// </summary>
public sealed class LogicAppSwitchRoleTests
{
    private const string Role = "infra/estate/logic-app-switch-role.bicep";
    private const string DriverReports = "infra/estate/driver-reports-logic-app.bicep";
    private const string PortalPrincipal = "00000000-0000-0000-0000-0000000000a1";
    private const string RoleId = "/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.Authorization/roleDefinitions/11111111-1111-1111-1111-111111111111";
    private const string ResourceGroupId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg";

    [Fact]
    public void TheRole_CanOnlyReadStartAndStopASite_InTheEstatesResourceGroup()
    {
        var definition = BicepSnapshot.Resources(Role, $"param assignableScope = '{ResourceGroupId}'")
            .Single(resource => Type(resource) == "Microsoft.Authorization/roleDefinitions")["properties"]!;

        Strings(definition["permissions"]![0]!["actions"]).Should().BeEquivalentTo(
            "Microsoft.Web/sites/read", "Microsoft.Web/sites/start/action", "Microsoft.Web/sites/stop/action");
        Strings(definition["permissions"]![0]!["notActions"]).Should().BeEmpty();
        Strings(definition["permissions"]![0]!["dataActions"]).Should().BeEmpty();
        Strings(definition["permissions"]![0]!["notDataActions"]).Should().BeEmpty();
        Strings(definition["assignableScopes"]).Should().Equal(ResourceGroupId);
    }

    [Fact]
    public void ThePartnerPortalHoldsTheRole_OnTheDriverReportsLogicApp_AndNowhereElseInThatModule()
    {
        var assignments = BicepSnapshot.Resources(DriverReports,
            [
                "param location = 'westeurope'", "param tags = {}", "param integrationRetentionDays = 90", "param driverReportsIdentityName = 'id-driver-reports'",
                "param tmsApiClientId = 'tms'", "param tmsBaseUrl = 'https://tms'", "param driverAppBaseUrl = 'https://driver-app'", "param driverAppApiClientId = 'driver-app'",
                "param driverProofsContainerName = 'proofs'", "param driverProofsBlobEndpoint = 'https://blobs/'", "param applicationInsightsName = 'appi'",
                "param serviceBusName = 'sb'", "param planName = 'plan'", "param contentKeyVaultName = 'kv-content'",
                "param integrationConsolePrincipalId = '00000000-0000-0000-0000-000000000001'",
                $"param partnerPortalPrincipalId = '{PortalPrincipal}'", $"param logicAppSwitchRoleId = '{RoleId}'",
            ])
            .Where(resource => Type(resource) == "Microsoft.Authorization/roleAssignments" && resource["properties"]!["principalId"]!.GetValue<string>() == PortalPrincipal)
            .ToList();

        var assignment = assignments.Should().ContainSingle().Subject;
        assignment["properties"]!["roleDefinitionId"]!.GetValue<string>().Should().Be(RoleId);
        assignment["id"]!.GetValue<string>().Should().Contain("/providers/Microsoft.Web/sites/logic-driver-reports-", "the role is held on the Logic App, not on the resource group");
    }

    [Fact]
    public void OnlyTheTemplatesThatNeedItTakeThePartnerPortalsPrincipal()
    {
        var takers = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "infra"), "*.bicep", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("partnerPortalPrincipalId", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/'))
            .Order()
            .ToList();

        // The identity itself, its own Key Vault (the Personas' keys) and the one Logic App it switches. A new line here is a new right for the presenter panel's identity.
        takers.Should().Equal("infra/estate.bicep", DriverReports, "infra/estate/identities.bicep", "infra/estate/partner-api.bicep");
    }

    private static string Type(JsonNode resource) => resource["type"]!.GetValue<string>();

    private static List<string> Strings(JsonNode? array) => array?.AsArray().Select(item => item!.GetValue<string>()).ToList() ?? [];
}
