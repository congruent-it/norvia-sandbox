using System.Text.RegularExpressions;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// What the estate Bicep sets up for Carrier invoices (E2): who may submit them, who may call the Logic App's trigger and who may
/// receive them at the ERP. Entra and API Management run only in Azure, so these tests read the Bicep; the smoke run calls the estate.
/// </summary>
public sealed class CarrierInvoiceEstateTests
{
    private static readonly string Identities = Contracts.Read("Estate/identities.bicep");
    private static readonly string PartnerApi = Contracts.Read("Estate/partner-api.bicep");

    // A resource block, from its declaration to the closing brace at the start of a line.
    private static string Block(string bicep, string declaration)
    {
        var match = Regex.Match(bicep, $@"(?ms)^{Regex.Escape(declaration)}.*?^(\}}|\])$");
        return match.Success ? match.Value : throw new InvalidOperationException($"{declaration} is not declared.");
    }

    [Fact]
    public void ThePartnerApi_HasACarrierInvoicesSubmitAppRole_ForApplications()
    {
        var partnerApi = Block(Identities, "resource partnerApi 'Microsoft.Graph/applications@v1.0'");

        partnerApi.Should().Contain("id: carrierInvoicesSubmitRoleId").And.Contain("value: carrierInvoicesSubmitRole");
        Identities.Should().Contain("var carrierInvoicesSubmitRole = 'CarrierInvoices.Submit'");
        Block(partnerApi, "  appRoles").Should().Contain("allowedMemberTypes");
    }

    [Fact]
    public void OnlyCarrierPersonas_AreGrantedCarrierInvoicesSubmit()
    {
        var grants = Block(Identities, "resource carrierInvoicesSubmitRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0'");

        grants.Should().Contain("persona.party.type == 'Carrier'").And.NotContain("'Shipper'");
        grants.Should().Contain("resourceId: partnerApiPrincipal.id").And.Contain("appRoleId: carrierInvoicesSubmitRoleId").And.Contain("principalId: personaPrincipals[i].id");
        Regex.Matches(Identities, @"appRoleId: carrierInvoicesSubmitRoleId").Should().ContainSingle("no one else holds the role");
    }

    [Fact]
    public void TheCarrierInvoiceIdentity_HoldsOnlyTheErpReceiveRole()
    {
        var grants = Regex.Matches(Identities, @"(?ms)^resource \w+ 'Microsoft.Graph/appRoleAssignedTo@v1.0' = \{.*?^\}$")
            .Where(grant => grant.Value.Contains("carrierInvoiceIdentity.properties.principalId", StringComparison.Ordinal))
            .ToList();

        grants.Should().ContainSingle();
        grants[0].Value.Should().Contain("resourceId: erpApiPrincipal.id").And.Contain("appRoleId: carrierInvoicesReceiveRoleId");
        Regex.Matches(Identities, @"appRoleId: carrierInvoicesReceiveRoleId").Should().ContainSingle("only the Carrier invoice identity holds the role");
    }

    [Fact]
    public void OnlyApiManagement_MayCallTheCarrierInvoiceTrigger()
    {
        var grant = Block(Identities, "resource carrierInvoiceTriggerRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0'");
        var principal = Block(Identities, "resource carrierInvoiceTriggerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0'");

        grant.Should().Contain("principalId: apiManagementIdentity.properties.principalId");
        principal.Should().Contain("appRoleAssignmentRequired: true");
        Regex.Matches(Identities, @"appRoleId: carrierInvoiceTriggerRoleId").Should().ContainSingle();
    }

    [Fact]
    public void TheSubmitOperation_IsInThePartnerApi_WithItsPolicy()
    {
        var operation = Block(PartnerApi, "resource submitCarrierInvoice 'Microsoft.ApiManagement/service/apis/operations@2024-05-01'");
        var policy = Block(PartnerApi, "resource submitCarrierInvoicePolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01'");

        operation.Should().Contain("method: 'POST'").And.Contain("urlTemplate: '/carrier-invoices'");
        policy.Should().Contain("loadTextContent('partner-api/submit-carrier-invoice.policy.xml')").And.Contain("carrierInvoiceLogicApp");
    }

    [Fact]
    public void TheBackend_IsTheCarrierInvoiceLogApp_AndThePolicyReadsItsAudience()
    {
        Block(PartnerApi, "resource carrierInvoiceLogicApp 'Microsoft.ApiManagement/service/backends@2024-05-01'")
            .Should().Contain("name: 'carrier-invoice-logic-app'").And.Contain("carrierInvoiceLogicAppHostName");
        PartnerApi.Should().Contain("'carrier-invoice-trigger-audience': carrierInvoiceTriggerAudience");
        Regex.Matches(PartnerApi, "'carrier-invoice-trigger-audience'").Should().HaveCount(2, "as a named value and in the list the values are created from");
    }
}
