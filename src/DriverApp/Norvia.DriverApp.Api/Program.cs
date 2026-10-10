using System.Text.Json.Serialization;
using Mediator;
using Norvia.DriverApp.Api.Settings;
using Norvia.DriverApp.Api.Trips;
using Norvia.DriverApp.Application.Trips.Receive;
using Norvia.DriverApp.Domain.Trips;
using Norvia.DriverApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDriverAppInfrastructure();
builder.AddDriverAppSeedDrivers();

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(ReceiveTripVersionCommand), typeof(DriverTrip)];
});

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDefaultEndpoints();
app.MapTrips();
app.MapSettings();

await app.RunAsync();
