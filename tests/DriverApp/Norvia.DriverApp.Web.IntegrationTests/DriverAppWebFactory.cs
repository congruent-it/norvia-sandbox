using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>
/// Runs the Driver app's screens in memory with the Trips of <see cref="Trips"/> in place of the database, signed in the way
/// the environment says: Easy Auth's header in Azure, or the stand-in locally (ADR 0037).
/// </summary>
public sealed class DriverAppWebFactory(string environment, bool standIn) : WebApplicationFactory<Program>
{
    public const string AnoukId = "DRV-0001";
    public const string JoostId = "DRV-0002";

    /// <summary>The seed file the Driver app reads its Drivers from, as the AppHost points at it.</summary>
    private static readonly string SeedFile = Path.Combine(AppContext.BaseDirectory, "norvia.seed.json");

    public List<DriverTrip> Trips { get; } =
    [
        Trip("TR-0000001", AnoukId, "Anouk de Vries", new DateOnly(2026, 10, 14)),
        Trip("TR-0000002", AnoukId, "Anouk de Vries", new DateOnly(2026, 10, 13)),
        Trip("TR-0000003", JoostId, "Joost Bakker", new DateOnly(2026, 10, 14)),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Seed:Path", SeedFile);
        builder.UseSetting("ConnectionStrings:driver-app", "Server=unused;Database=unused");
        if (standIn)
        {
            builder.UseSetting("LocalSignIn:Enabled", "true");
        }

        builder.ConfigureTestServices(services =>
        {
            var repository = Substitute.For<IReadRepository<DriverTrip>>();
            repository.ListAsync(Arg.Any<ISpecification<DriverTrip, DriverTripRow>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<ISpecification<DriverTrip, DriverTripRow>>().Evaluate(Trips).ToList());
            services.AddScoped(_ => repository);
        });
    }

    private static DriverTrip Trip(string number, string driverId, string driverName, DateOnly day) =>
        new(new TripNumber(number), 1, new DriverId(driverId),
            new TripDetails(day, new TimeOnly(6, 0), TimeSpan.FromHours(5), new TripVehicle("VEH-0001", "NV-123-A", "RigidTruck"), driverName, "corr-1", []));
}
