using System.Text.RegularExpressions;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// What the estate Bicep sets up for the Shipper tracking API (P8): who may read tracking, and who may read it from the TMS.
/// Entra and API Management run only in Azure, so these tests read the Bicep; the smoke run calls the estate.
/// </summary>
public sealed class ShipperTrackingEstateTests
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
    public void ThePartnerApi_HasATrackingReadAppRole_ForApplications()
    {
        var partnerApi = Block(Identities, "resource partnerApi 'Microsoft.Graph/applications@v1.0'");

        partnerApi.Should().Contain("id: trackingReadRoleId").And.Contain("value: trackingReadRole");
        Identities.Should().Contain("var trackingReadRole = 'Tracking.Read'");
        Block(partnerApi, "  appRoles").Should().Contain("allowedMemberTypes");
    }

    [Fact]
    public void OnlyShipperPersonas_AreGrantedTrackingRead()
    {
        var grants = Block(Identities, "resource trackingReadRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0'");

        grants.Should().Contain("persona.party.type == 'Shipper'").And.NotContain("'Carrier'");
        grants.Should().Contain("resourceId: partnerApiPrincipal.id").And.Contain("appRoleId: trackingReadRoleId").And.Contain("principalId: personaPrincipals[i].id");
    }

    [Fact]
    public void TrackingReadIsGrantedToNoOtherPrincipal()
    {
        Regex.Matches(Identities, @"appRoleId: trackingReadRoleId").Should().ContainSingle("only the Shipper Personas' grant names the role");
    }

    [Fact]
    public void OnlyApiManagementsIdentity_HoldsTheTmsShipperTrackingReadRole()
    {
        Regex.Matches(Identities, @"appRoleId: shipperTrackingReadRoleId").Should().ContainSingle("no one else holds the role");
        var grant = Block(Identities, "resource shipperTrackingReadRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0'");

        grant.Should().Contain("resourceId: tmsApiPrincipal.id").And.Contain("principalId: apiManagementIdentity.properties.principalId");
    }

    [Theory]
    [InlineData("list-shipper-transport-orders", "GET", "/transport-orders")]
    [InlineData("find-shipper-transport-order", "GET", "/transport-orders/find")]
    [InlineData("get-shipper-transport-order", "GET", "/transport-orders/{transportOrderNumber}")]
    [InlineData("get-shipper-proof-of-delivery-file", "GET", "/transport-orders/{transportOrderNumber}/proof-of-delivery/files/{fileId}")]
    public void TheTrackingOperation_IsInThePartnerApi_WithItsPolicy(string name, string method, string urlTemplate)
    {
        var operation = Block(PartnerApi, $"resource {Camel(name)} 'Microsoft.ApiManagement/service/apis/operations@2024-05-01'");

        operation.Should().Contain($"name: '{name}'").And.Contain($"method: '{method}'").And.Contain($"urlTemplate: '{urlTemplate}'");
        Block(PartnerApi, $"resource {Camel(name)}Policy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01'")
            .Should().Contain($"partner-api/{name}.policy.xml").And.Contain("tmsApi");
    }

    [Fact]
    public void TheListOperation_TakesTheFiltersAndPaging_AsQueryParameters()
    {
        var list = Block(PartnerApi, "resource listShipperTransportOrders 'Microsoft.ApiManagement/service/apis/operations@2024-05-01'");

        list.Should().Contain("trackingStatus").And.Contain("collectionFrom").And.Contain("collectionTo").And.Contain("page").And.Contain("pageSize");
    }

    [Fact]
    public void ThePolicies_GetTheLiquidMaps_EscapedForXml()
    {
        PartnerApi.Should().Contain("loadTextContent('partner-api/shipper-tracking.liquid')").And.Contain("loadTextContent('partner-api/shipper-tracking-list.liquid')");
        PartnerApi.Should().Contain("[[shipper-tracking.liquid]]").And.Contain("[[shipper-tracking-list.liquid]]");
        PartnerApi.Should().Contain("'&lt;'", "a template's < must not break the policy's XML");
    }

    [Fact]
    public void TheTmsApi_IsABackendAndNamedValues_ForThePolicies()
    {
        Block(PartnerApi, "resource tmsApi 'Microsoft.ApiManagement/service/backends@2024-05-01'").Should().Contain("name: 'tms-api'");
        PartnerApi.Should().Contain("'tms-api-client-id': tmsApiClientId");
    }

    private static string Camel(string kebab) =>
        string.Concat(kebab.Split('-').Select((part, index) => index == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
}
