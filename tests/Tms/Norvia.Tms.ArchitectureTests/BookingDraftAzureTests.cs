using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Tms.ArchitectureTests;

/// <summary>
/// The Intake agent's Booking draft in Azure (#459, ADR 0040): the app role on the Partner API, the host's API with its one role that only API
/// Management holds, the host's identity with Foundry User on the Intake project only, the AppHost wiring and the scripts that publish the Agent
/// and smoke-test the operation. The Bicep is evaluated locally with <c>bicep snapshot</c>; nothing is deployed. The policy is in
/// <see cref="BookingDraftPolicyTests"/>.
/// </summary>
public sealed class BookingDraftAzureTests
{
    private const string Module = "infra/estate/booking-draft.bicep";
    private const string Role = "BookingDrafts.Create";
    private const string ApimPrincipal = "00000000-0000-0000-0000-000000000001";

    private static readonly string[] ApplicationOnly = ["Application"];

    private static List<JsonNode> Resources(params string[] extra) =>
        BicepSnapshot.Resources(
            Module,
            [
                "param environmentName = 'env'", "param location = 'westeurope'", "param tags = {}", "param apiManagementName = 'apim'",
                $"param apiManagementPrincipalId = '{ApimPrincipal}'", .. extra,
            ]);

    private static IEnumerable<JsonNode> Of(List<JsonNode> resources, string type) => resources.Where(resource => resource["type"]!.GetValue<string>() == type);

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Path, path));

    [Fact]
    public void TheHostsApi_HasOneRole_ForApplicationsOnly_AndEntraIssuesNoTokenWithoutIt()
    {
        var resources = Resources();
        var app = Of(resources, "Microsoft.Graph/applications@v1.0").Single()["properties"]!;
        var roles = app["appRoles"]!.AsArray().Select(role => role!).ToList();

        app["displayName"]!.GetValue<string>().Should().Be("Norvia booking draft API (env)", "provision-estate.ps1 looks the registration up by this name");
        roles.Select(role => role["value"]!.GetValue<string>()).Should().Equal(Role);
        roles.Should().OnlyContain(role => role["allowedMemberTypes"]!.AsArray().Select(type => type!.GetValue<string>()).SequenceEqual(ApplicationOnly));
        app["api"]!["requestedAccessTokenVersion"]!.GetValue<int>().Should().Be(2, "azp is the caller's client ID in a v2 token");
        Of(resources, "Microsoft.Graph/servicePrincipals@v1.0").Single()["properties"]!["appRoleAssignmentRequired"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void OnlyApiManagementsIdentity_HoldsTheRole_SoOnlyApiManagementCanCallTheHost()
    {
        var assignments = Of(Resources(), "Microsoft.Graph/appRoleAssignedTo@v1.0").ToList();

        assignments.Should().ContainSingle().Which["properties"]!["principalId"]!.GetValue<string>().Should().Be(ApimPrincipal);
    }

    [Fact]
    public void TheHostsIdentity_HoldsNoAzureRoleInThisModule_AndItsNameIsTheOneTheScriptsLookFor()
    {
        var resources = Resources();

        Of(resources, "Microsoft.ManagedIdentity/userAssignedIdentities").Single()["name"]!.GetValue<string>().Should().StartWith("id-booking-draft-");
        Of(resources, "Microsoft.Authorization/roleAssignments").Should().BeEmpty("Foundry User on the Intake project is given by foundry.bicep");
    }

    [Fact]
    public void TheOperation_IsAPostOnThePartnerApi_WithThePolicyOfTheFile_AndTheBackendIsAPlaceholderUntilTheHostIsDeployed()
    {
        var resources = Resources();
        var operation = Of(resources, "Microsoft.ApiManagement/service/apis/operations").Single()["properties"]!;
        var policy = Of(resources, "Microsoft.ApiManagement/service/apis/operations/policies").Single()["properties"]!;

        operation["method"]!.GetValue<string>().Should().Be("POST");
        operation["urlTemplate"]!.GetValue<string>().Should().Be("/booking-drafts");
        policy["format"]!.GetValue<string>().Should().Be("rawxml");
        policy["value"]!.GetValue<string>().Should().Be(Read("infra/estate/partner-api/create-booking-draft.policy.xml"));
        Of(resources, "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>().Should().Be("https://booking-draft-host.invalid");
        Of(Resources("param bookingDraftBaseUrl = 'https://booking-draft-api.example.io'"), "Microsoft.ApiManagement/service/backends").Single()["properties"]!["url"]!.GetValue<string>()
            .Should().Be("https://booking-draft-api.example.io");
    }

    [Fact]
    public void ThePartnerApi_HasTheDraftRole_GrantedToShipperPersonasOnly()
    {
        var identities = Read("infra/estate/identities.bicep");
        var grants = Regex.Match(identities, @"(?ms)^resource draftRoleGrants .*?^\]$").Value;

        identities.Should().Contain("var draftRole = 'TransportOrders.Draft'");
        identities.Should().Contain("value: draftRole");
        grants.Should().Contain("persona.party.type == 'Shipper'").And.Contain("resourceId: partnerApiPrincipal.id").And.Contain("appRoleId: draftRoleId");
    }

    [Fact]
    public void TheHostsIdentity_IsGivenFoundryUserOnTheIntakeProjectOnly()
    {
        var estate = Read("infra/estate.bicep");
        var foundry = Read("infra/estate/foundry.bicep");
        var applications = estate[estate.IndexOf("applicationPrincipalIds:", StringComparison.Ordinal)..];
        applications = applications[..applications.IndexOf('\n', StringComparison.Ordinal)];

        estate.Should().Contain("intakeApplicationPrincipalIds: [bookingDraft.outputs.principalId]");
        applications.Should().NotContain("bookingDraft", "the norvia project is never the Booking draft host's");
        Regex.Match(foundry, @"(?ms)^resource applicationsUseTheIntakeProject .*?^\}\]$").Value.Should().Contain("scope: intakeProject").And.Contain("roleDefinitionId: foundryUser");
    }

    [Fact]
    public void TheEstate_CallsTheModuleAfterTheMcpServers_KeepsItsAddress_AndStaysWithinItsOutputs()
    {
        var estate = Read("infra/estate.bicep");

        estate.Should().Contain("module bookingDraft 'estate/booking-draft.bicep'").And.Contain("bookingDraftBaseUrl: bookingDraftBaseUrl");
        Regex.Match(estate, @"(?ms)^module bookingDraft .*?^\}$").Value.Should().Contain("financeRejectionReasonMcp", "API Management refuses two writes at once");
        Read("infra/estate.bicepparam").Should().Contain("ESTATE_BOOKING_DRAFT_BASE_URL");
        Regex.Count(estate, @"(?m)^output ").Should().BeLessThanOrEqualTo(64, "a template may have 64 outputs");
    }

    [Fact]
    public void TheAppHost_RunsTheHostAsItsOwnIdentity_AskingTheIntakeProject_ForTheHostsApiAndOnlyApiManagement_OutsideEasyAuth()
    {
        var host = Read("src/Norvia.AppHost/AppHost.cs");
        var block = host[host.IndexOf("builder.AddProject<Projects.Norvia_Integration_BookingDraft_Api>", StringComparison.Ordinal)..];
        block = block[..block.IndexOf(";\n", StringComparison.Ordinal)];

        block.Should().Contain("WithAzureUserAssignedIdentity(bookingDraftIdentity)");
        block.Should().Contain("Agents__Foundry__ProjectEndpoint\", estate.FoundryIntakeProjectEndpoint").And.NotContain("estate.FoundryProjectEndpoint");
        block.Should().Contain("Agents__Api__Audience\", estate.BookingDraftApiClientId").And.Contain("Agents__Api__GatewayClientId\", estate.ApiManagementClientId");
        block.Should().NotContain("EntraSignIn", "the host is no browser's: it takes API Management's token and checks the role itself");
        host.IndexOf("Norvia_Integration_BookingDraft_Api", StringComparison.Ordinal).Should().BeGreaterThan(host.IndexOf("if (builder.ExecutionContext.IsPublishMode)", StringComparison.Ordinal), "locally the stand-in plays the operation");
    }

    [Fact]
    public void TheDeployScript_PublishesTheIntakeAgentIntoTheIntakeProject_AndPointsTheHostsBackendAtTheHost()
    {
        var script = Read("infra/deploy-agents.ps1");

        script.Should().Contain("$intakeAgents = @('booking-intake')").And.Contain("FOUNDRY_INTAKE_PROJECT_ENDPOINT");
        script.Should().Contain("booking-draft-host").And.Contain("ESTATE_BOOKING_DRAFT_BASE_URL");
        script.Should().MatchRegex(@"-ProjectEndpoint \$projectEndpoint");
        Read("infra/provision-estate.ps1").Should().Contain("Norvia booking draft API (").And.Contain("estate_booking_draft_api_client_id").And.Contain("estate_foundry_intake_project_endpoint");
    }

    [Fact]
    public void TheIntakeAgent_HasNoTools_SoItHasNoConnectionAndNoRole()
    {
        var definition = Read("agents/booking-intake.agent.yaml");

        definition.Should().NotContain("server:");
        Read("infra/deploy-agents.ps1").Should().Contain("An Agent with no tool reads nothing, so its identity needs no role.");
    }

    [Fact]
    public void SmokeRun_ChecksTheDraftTheRoleTheCapTheHostsDoorTheHopsNoTextAndTheRoles()
    {
        var script = Read("infra/smoke-test-booking-draft.ps1");

        script.Should().Contain("Get-PersonaClientId brouwer-dranken").And.Contain("Get-PersonaClientId van-dijk-transport");
        script.Should().Contain("-ne 403").And.Contain("-ne 413").And.Contain("directWithoutToken").And.Contain("directWithPersonaToken");
        script.Should().Contain("booking-draft-api").And.Contain("agentId").And.Contain("search in (AppTraces, AppRequests, AppDependencies, AppEvents, AppExceptions)");
        script.Should().Contain("hold exactly Foundry User on the Intake project");
        script.Should().Contain("/norvia-intake").And.Contain("appRoleAssignments");
    }

    [Fact]
    public void SmokeRun_NeverEchoesATokenAKeyTheEmailOrTheDraft()
    {
        var lines = Read("infra/smoke-test-booking-draft.ps1").Split('\n').Where(line => line.TrimStart().StartsWith("Write-", StringComparison.Ordinal));

        lines.Should().NotContain(line => line.Contains("token", StringComparison.OrdinalIgnoreCase) && !line.Contains("without a token", StringComparison.Ordinal) && !line.Contains("Persona''s token", StringComparison.Ordinal) && !line.Contains("with a token", StringComparison.Ordinal) && !line.Contains("with a Persona", StringComparison.Ordinal));
        lines.Should().NotContain(line => line.Contains("$text", StringComparison.Ordinal) || line.Contains("$body", StringComparison.Ordinal) || line.Contains("$consignee", StringComparison.Ordinal) || line.Contains("Key", StringComparison.Ordinal));
    }
}
