using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Integration.LocalSupport;

namespace Norvia.Integration.LocalCarrierInvoiceWorker.UnitTests;

/// <summary>The trigger takes API Management alone as the workflow's first step does, and the Carrier from its header, never the invoice.</summary>
public class CarrierInvoiceTriggerTests
{
    private static readonly string Invoice = CarrierInvoiceWorld.Sample("two-lines.carrier-invoice.json");

    [Fact]
    public async Task Handle_ACallerOtherThanApiManagement_IsRefused403_AndNothingIsMappedOrSent()
    {
        var world = new CarrierInvoiceWorld();

        var (answer, _) = await HandleAsync(world, client: "norvia-local-persona-van-dijk-transport");

        answer.StatusCode.Should().Be(403);
        answer.ResponseContent.Should().Be("Only API Management may call this workflow.");
        world.Erp.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithoutAToken_IsRefused403()
    {
        var world = new CarrierInvoiceWorld();

        var (answer, _) = await HandleAsync(world, client: null);

        answer.StatusCode.Should().Be(403);
        world.Erp.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ATokenOfAnotherAudience_IsRefused403()
    {
        var world = new CarrierInvoiceWorld();

        var (answer, _) = await HandleAsync(world, client: "api-management", audience: "erp");

        answer.StatusCode.Should().Be(403);
        world.Erp.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ApiManagement_RunsTheFlowForTheCarrierOfTheHeader_AndAnswersWithItsStatusAndBody()
    {
        var world = new CarrierInvoiceWorld();
        world.Erp.Otherwise(HttpStatusCode.Created, CarrierInvoiceWorld.ErpReceived());

        var (answer, context) = await HandleAsync(world, client: "api-management");

        answer.StatusCode.Should().Be(201);
        answer.ResponseContent.Should().Contain("\"RECEIVED\"");
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be(CarrierInvoiceWorld.CorrelationId);
        world.Erp.Requests.Single().Body.Should().Contain("\"carrierId\":\"CAR-0001\"");
    }

    private static async Task<(ContentHttpResult Answer, HttpContext Context)> HandleAsync(CarrierInvoiceWorld world, string? client, string audience = "trigger")
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Invoice));
        context.Request.Headers["X-Carrier-ID"] = "CAR-0001";
        context.Request.Headers["X-Correlation-ID"] = CarrierInvoiceWorld.CorrelationId;
        if (client is not null)
        {
            context.Request.Headers.Authorization = "Bearer " + TmsTokens.Create(
                new LocalTokenOptions { Issuer = "issuer", Audience = audience, SigningKey = CarrierInvoiceWorld.Signing }, TimeProvider.System, client, "CarrierInvoice.Trigger");
        }

        var answer = await world.RunAsync(() => CarrierInvoiceTrigger.HandleAsync(context, world.Flow, world.Options, NullLoggerFactory.Instance));
        return (answer, context);
    }
}
