using AngleSharp.Dom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests;

/// <summary>Renders the portal's components with the application behind <see cref="IPartnerPortal"/> substituted.</summary>
public abstract class PortalWebContext : BunitContext
{
    protected PortalWebContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Portal);
        Services.AddSingleton<TimeProvider>(Time);
        Portal.ListPersonasAsync(Arg.Any<CancellationToken>()).Returns(Samples.Personas);
        Portal.GetCarrierAnswerSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierAnswerSettingsView(CarrierAnswerMode.Manual, CarrierRejectionReason.NoCapacity, 5));
        Portal.GetCarrierTrackingSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CarrierTrackingSettingsView(TrackingMode.Manual, 5));
    }

    protected FakeTimeProvider Time { get; } = new();

    protected IPartnerPortal Portal { get; } = Substitute.For<IPartnerPortal>();

    /// <summary>The input labelled so, inside the element given (such as a fieldset), found as a person finds it: by its label.</summary>
    protected static IElement Field(IElement within, string label)
    {
        ArgumentNullException.ThrowIfNull(within);
        var labelElement = within.QuerySelectorAll("label").SingleOrDefault(element => element.TextContent.Trim() == label)
            ?? throw new InvalidOperationException($"There is no field labelled '{label}'.");
        return within.QuerySelector($"#{labelElement.GetAttribute("for")}")
            ?? throw new InvalidOperationException($"The label '{label}' labels nothing.");
    }

    /// <summary>The fieldset whose legend is this.</summary>
    protected static IElement Group<TComponent>(IRenderedComponent<TComponent> page, string legend)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.FindAll("fieldset").SingleOrDefault(fieldset => fieldset.QuerySelector("legend")?.TextContent.Trim() == legend)
            ?? throw new InvalidOperationException($"There is no group '{legend}'.");
    }
}
