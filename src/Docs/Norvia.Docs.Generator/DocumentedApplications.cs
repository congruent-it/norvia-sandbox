using System.Diagnostics.CodeAnalysis;
using Norvia.DriverApp.Api.Trips;
using Norvia.DriverApp.Infrastructure.Data;
using Norvia.Erp.Api.ShipperInvoices;
using Norvia.Erp.Infrastructure.Data;
using Norvia.PartnerPortal.Infrastructure.Data;
using Norvia.Simulator.Infrastructure.Data;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Docs.Generator;

/// <summary>Every application, and what the generator can write for it. A new application with a database or an API that publishes OpenAPI is added here.</summary>
[ExcludeFromCodeCoverage(Justification = "Wiring to the applications; the drift tests run it.")]
internal static class DocumentedApplications
{
    public static IReadOnlyList<DocumentedApplication> All { get; } =
    [
        new("tms", TmsDatabaseModel.Create, () => OpenApiDocuments.ReadAsync<TrackingScopeMiddleware>("src/Tms/Norvia.Tms.Api")),
        new("erp", ErpDatabaseModel.Create, () => OpenApiDocuments.ReadAsync<ShipperInvoiceNotDeliveredRequest>("src/Erp/Norvia.Erp.Api")),
        new("partner-portal", PartnerPortalDatabaseModel.Create),
        new("driver-app", DriverAppDatabaseModel.Create, () => OpenApiDocuments.ReadAsync<TripDriverRequest>("src/DriverApp/Norvia.DriverApp.Api")),
        new("simulator", SimulatorDatabaseModel.Create),
        new("integration-console"),
    ];
}
