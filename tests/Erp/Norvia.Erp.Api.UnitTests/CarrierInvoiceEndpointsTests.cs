using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.CarrierInvoices;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Receive;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CarrierInvoiceEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public CarrierInvoiceEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<ReceiveCarrierInvoiceResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<ReceiveCarrierInvoiceResult>>>>()(_mediator));

    private static CarrierInvoiceView View() => new(
        "CAR-0001", "VDT-2026-0412", new DateOnly(2026, 10, 19), "EUR", 90.50m,
        [new CarrierInvoiceLineView("TRX-998", 90.50m, null, "Matched", "Matched", "CB-0000123", 90.50m)],
        "invoice-1", DateTimeOffset.UnixEpoch, "Approved", 90.50m, DateTimeOffset.UnixEpoch);

    private void Answer(Result<ReceiveCarrierInvoiceResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReceiveCarrierInvoiceCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public void Receive_IsGuardedByTheReceiveRoleOnly() => CarrierInvoiceEndpoints.ReceiveRole.Should().Be("CarrierInvoices.Receive");

    [Fact]
    public async Task Receive_AnInvalidBody_AnswersAValidationProblemWithoutReceiving()
    {
        var result = await CarrierInvoiceEndpoints.ReceiveAsync(null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<ReceiveCarrierInvoiceResult>>(default!);
    }

    [Fact]
    public async Task Receive_ANewInvoice_AnswersCreated()
    {
        Answer(new ReceiveCarrierInvoiceResult(View(), Repeat: false));

        var result = await CarrierInvoiceEndpoints.ReceiveAsync(CarrierInvoiceValidatorTests.Valid, _retrier, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<ReceiveCarrierInvoiceResult>>().Subject;
        created.Location.Should().Be("/v1/carrier-invoices/CAR-0001/VDT-2026-0412");
        created.Value!.Repeat.Should().BeFalse();
    }

    [Fact]
    public async Task Receive_ARepeat_AnswersOkWithTheOriginalFlaggedAsARepeat()
    {
        Answer(new ReceiveCarrierInvoiceResult(View(), Repeat: true));

        var result = await CarrierInvoiceEndpoints.ReceiveAsync(CarrierInvoiceValidatorTests.Valid, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ReceiveCarrierInvoiceResult>>().Which.Value!.Repeat.Should().BeTrue();
    }

    [Theory]
    [InlineData(CarrierInvoiceRefusalCode.CarrierNotFound, 422)]
    [InlineData(CarrierInvoiceRefusalCode.InvoiceNumberUsed, 409)]
    [InlineData(CarrierInvoiceRefusalCode.CarrierInvoiceWithoutLines, 400)]
    [InlineData(CarrierInvoiceRefusalCode.CarrierInvoiceLineWithoutReference, 400)]
    [InlineData(CarrierInvoiceRefusalCode.CarrierInvoiceLineAmountNotAboveZero, 400)]
    [InlineData(CarrierInvoiceRefusalCode.CarrierInvoiceReferenceRepeated, 400)]
    public async Task Receive_ARefusal_AnswersAProblemWithItsStableCodeAndCorrelationId(CarrierInvoiceRefusalCode code, int status)
    {
        Answer(CarrierInvoiceRefused.Because<ReceiveCarrierInvoiceResult>(new CarrierInvoiceRefusal(code, "readable message")));

        var result = await CarrierInvoiceEndpoints.ReceiveAsync(CarrierInvoiceValidatorTests.Valid, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Detail.Should().Be("readable message");
        problem.ProblemDetails.Extensions["code"].Should().Be(code.ToString());
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("invoice-1");
    }
}
