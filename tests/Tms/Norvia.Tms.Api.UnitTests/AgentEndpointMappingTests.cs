using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Api.Agents;
using NSubstitute;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>Each Agent's view is mapped under its own route prefix and nothing else (ADR 0040): one group per Agent, each behind its own role.</summary>
public sealed class AgentEndpointMappingTests
{
    private static string[] RoutesOf(Action<IEndpointRouteBuilder> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddApiVersioning();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        builder.Services.AddSingleton(TimeProvider.System);
        var app = builder.Build();

        map(app);

        return [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!)];
    }

    [Fact]
    public void ThePlannerNotificationAgent_ReadsTheTemplateAndTheShipperViewOnly() =>
        RoutesOf(app => app.MapPlannerNotificationAgent()).Should().BeEquivalentTo(
            "/v{version:apiVersion}/agent/planner-notification/exceptions/{exceptionNumber}/shipper-notification-template",
            "/v{version:apiVersion}/agent/planner-notification/transport-orders/{transportOrderNumber}/shipper-view");

    [Fact]
    public void ThePlannerAgent_IsMappedOnlyUnderItsOwnPrefix() =>
        RoutesOf(app => app.MapPlannerAgent()).Should().NotBeEmpty().And.OnlyContain(route => route.StartsWith("/v{version:apiVersion}/agent/planner/", StringComparison.Ordinal));

    [Fact]
    public void TheCustomerServiceAgent_IsMappedOnlyUnderItsOwnPrefix() =>
        RoutesOf(app => app.MapCustomerServiceAgent()).Should().NotBeEmpty().And.OnlyContain(route => route.StartsWith("/v{version:apiVersion}/agent/customer-service/", StringComparison.Ordinal));

    [Fact]
    public void TheCustomerServiceReplyAgent_IsMappedOnlyUnderItsOwnPrefix() =>
        RoutesOf(app => app.MapCustomerServiceReplyAgent()).Should().NotBeEmpty().And.OnlyContain(route => route.StartsWith("/v{version:apiVersion}/agent/", StringComparison.Ordinal));
}
