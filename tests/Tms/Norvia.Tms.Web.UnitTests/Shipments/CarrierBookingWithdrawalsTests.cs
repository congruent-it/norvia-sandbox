using Microsoft.Extensions.Logging.Abstractions;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class CarrierBookingWithdrawalsTests
{
    private readonly ITransportOrderCommands _commands = Substitute.For<ITransportOrderCommands>();
    private readonly CarrierBookingWithdrawals _withdrawals;

    public CarrierBookingWithdrawalsTests()
    {
        _withdrawals = new CarrierBookingWithdrawals(_commands, NullLogger.Instance);
    }

    [Fact]
    public async Task WithdrawAsync_GivesTheTmsApisAnswer()
    {
        _commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>()).Returns(new WithdrawAnswer.Refused("Not open."));

        var answer = await _withdrawals.WithdrawAsync("CB-0000007", Xunit.TestContext.Current.CancellationToken);

        answer.Should().Be(new WithdrawAnswer.Refused("Not open."));
    }

    [Fact]
    public async Task WithdrawAsync_WhenTheTmsCantBeReached_AnswersNull()
    {
        _commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>())
            .Returns<WithdrawAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));

        var answer = await _withdrawals.WithdrawAsync("CB-0000007", Xunit.TestContext.Current.CancellationToken);

        answer.Should().BeNull();
    }

    [Fact]
    public async Task WithdrawAsync_WhenCancelled_AnswersNull()
    {
        using var closed = new CancellationTokenSource();
        _commands.WithdrawAsync("CB-0000007", Arg.Any<CancellationToken>()).Returns<WithdrawAnswer>(_ =>
        {
            closed.Cancel();
            throw new OperationCanceledException(closed.Token);
        });

        var answer = await _withdrawals.WithdrawAsync("CB-0000007", closed.Token);

        answer.Should().BeNull();
    }
}
