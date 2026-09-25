using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Draft;
using Norvia.PartnerPortal.Application.Bookings.List;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.Personas.List;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class PartnerPortalRequestsTests : IDisposable
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ServiceProvider _services;
    private readonly PartnerPortalRequests _portal;

    public PartnerPortalRequestsTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _mediator).BuildServiceProvider();
        _portal = new PartnerPortalRequests(_services.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task ListPersonasAsync_ListsThePersonas()
    {
        _mediator.Send(Arg.Any<ListPersonasQuery>(), Arg.Any<CancellationToken>()).Returns(Samples.Personas);

        (await _portal.ListPersonasAsync(Xunit.TestContext.Current.CancellationToken)).Should().Equal(Samples.Personas);
    }

    [Fact]
    public async Task DraftBookingAsync_ForAPersona_IsItsDraft_AndNullForNone()
    {
        var draft = Samples.Draft();
        _mediator.Send(new DraftBookingQuery("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns(Result.Success(draft));
        _mediator.Send(new DraftBookingQuery("nobody"), Arg.Any<CancellationToken>()).Returns(Result<BookingRequest>.NotFound());

        (await _portal.DraftBookingAsync("brouwer-dranken", Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(draft);
        (await _portal.DraftBookingAsync("nobody", Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SubmitBookingAsync_SendsItAsThePersona_AndReturnsNorviasAnswer()
    {
        var booking = Samples.Draft();
        var answered = Samples.Answered(NorviaAnswer.Accepted("TO-0000042", "corr-1"));
        _mediator.Send(new SubmitBookingCommand("brouwer-dranken", booking), Arg.Any<CancellationToken>()).Returns(Result.Success(answered));

        (await _portal.SubmitBookingAsync("brouwer-dranken", booking, Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(answered);
    }

    [Fact]
    public async Task ListBookingsAsync_ListsThePersonasBookings()
    {
        IReadOnlyList<BookingView> bookings = [Samples.Answered(NorviaAnswer.NotReceived(null))];
        _mediator.Send(new ListBookingsQuery("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns(bookings);

        (await _portal.ListBookingsAsync("brouwer-dranken", Xunit.TestContext.Current.CancellationToken)).Should().BeSameAs(bookings);
    }

    [Fact]
    public async Task ARequestThatFails_Throws_RatherThanSayingThereIsNone()
    {
        _mediator.Send(new DraftBookingQuery("brouwer-dranken"), Arg.Any<CancellationToken>()).Returns(Result<BookingRequest>.Error("The seed file is gone."));

        var draft = () => _portal.DraftBookingAsync("brouwer-dranken", Xunit.TestContext.Current.CancellationToken);

        await draft.Should().ThrowAsync<InvalidOperationException>();
    }
}
