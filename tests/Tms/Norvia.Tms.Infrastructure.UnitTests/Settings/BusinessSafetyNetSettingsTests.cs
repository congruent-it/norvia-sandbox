using Microsoft.Extensions.Configuration;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Infrastructure.Exceptions;
using Norvia.Tms.Infrastructure.ProofsOfDelivery;
using Norvia.Tms.Infrastructure.Shipments;

namespace Norvia.Tms.Infrastructure.UnitTests.Settings;

/// <summary>
/// The TMS settings the as-found toggles flow into (docs/operations/as-found.md): the Stale thresholds (P5), the Proof of delivery missing
/// threshold (P6) and the Late delivery check (P7). Healthy without a setting; each switch changes only its own rules.
/// </summary>
public sealed class BusinessSafetyNetSettingsTests
{
    private static readonly DateTimeOffset Latest = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Window = new(Latest.AddHours(-4), Latest);

    [Fact]
    public void WithoutSettings_EverySafetyNetIsOn()
    {
        var configuration = Configuration();

        DependencyInjection.StaleRulesFor(Bind<ShipmentStaleOptions>(configuration, ShipmentStaleOptions.SectionName))
            .IsStale(ShipmentStatus.InTransit, null, null, Latest, Latest.AddHours(25)).Should().BeTrue();
        DependencyInjection.MissingRulesFor(Bind<ProofOfDeliveryOptions>(configuration, ProofOfDeliveryOptions.SectionName))
            .IsMissing(Latest, Latest.AddHours(49)).Should().BeTrue();
        DependencyInjection.LateDeliveryRulesFor(Bind<LateDeliveryOptions>(configuration, LateDeliveryOptions.SectionName))
            .HasWindowPassed(Window, Latest).Should().BeTrue();
    }

    [Fact]
    public void ThresholdsSetFalse_LeavesOnlyTheStaleThresholdsUnset()
    {
        var configuration = Configuration(("ShipmentStale:ThresholdsSet", "false"));

        DependencyInjection.StaleRulesFor(Bind<ShipmentStaleOptions>(configuration, ShipmentStaleOptions.SectionName))
            .IsStale(ShipmentStatus.InTransit, null, null, Latest, Latest.AddDays(30)).Should().BeFalse("a quiet Shipment is not flagged Stale");
        DependencyInjection.MissingRulesFor(Bind<ProofOfDeliveryOptions>(configuration, ProofOfDeliveryOptions.SectionName))
            .IsMissing(Latest, Latest.AddHours(49)).Should().BeTrue();
        DependencyInjection.LateDeliveryRulesFor(Bind<LateDeliveryOptions>(configuration, LateDeliveryOptions.SectionName))
            .HasWindowPassed(Window, Latest).Should().BeTrue();
    }

    [Fact]
    public void MissingThresholdSetFalse_LeavesOnlyTheProofOfDeliveryMissingThresholdUnset()
    {
        var configuration = Configuration(("ProofsOfDelivery:MissingThresholdSet", "false"));

        DependencyInjection.MissingRulesFor(Bind<ProofOfDeliveryOptions>(configuration, ProofOfDeliveryOptions.SectionName))
            .IsMissing(Latest, Latest.AddDays(30)).Should().BeFalse();
        DependencyInjection.StaleRulesFor(Bind<ShipmentStaleOptions>(configuration, ShipmentStaleOptions.SectionName))
            .IsStale(ShipmentStatus.InTransit, null, null, Latest, Latest.AddHours(25)).Should().BeTrue();
        DependencyInjection.LateDeliveryRulesFor(Bind<LateDeliveryOptions>(configuration, LateDeliveryOptions.SectionName))
            .HasWindowPassed(Window, Latest).Should().BeTrue();
    }

    [Fact]
    public void CheckEnabledFalse_SwitchesOnlyTheLateDeliveryCheckOff()
    {
        var configuration = Configuration(("LateDelivery:CheckEnabled", "false"));

        DependencyInjection.LateDeliveryRulesFor(Bind<LateDeliveryOptions>(configuration, LateDeliveryOptions.SectionName))
            .HasWindowPassed(Window, Latest.AddYears(1)).Should().BeFalse();
        DependencyInjection.StaleRulesFor(Bind<ShipmentStaleOptions>(configuration, ShipmentStaleOptions.SectionName))
            .IsStale(ShipmentStatus.InTransit, null, null, Latest, Latest.AddHours(25)).Should().BeTrue();
        DependencyInjection.MissingRulesFor(Bind<ProofOfDeliveryOptions>(configuration, ProofOfDeliveryOptions.SectionName))
            .IsMissing(Latest, Latest.AddHours(49)).Should().BeTrue();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value))).Build();

    private static T Bind<T>(IConfiguration configuration, string section) where T : new() =>
        configuration.GetSection(section).Get<T>() ?? new T();
}
