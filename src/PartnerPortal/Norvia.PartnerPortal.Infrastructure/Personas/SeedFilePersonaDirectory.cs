using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Infrastructure.Personas;

/// <summary>
/// The Personas the portal plays, loaded once from the seed file, the one source of Shippers, Carriers and Personas.
/// A Shipper Persona's address book is its Shipper's addresses there.
/// </summary>
internal sealed class SeedFilePersonaDirectory : IPersonaDirectory
{
    // What a Carrier Persona without a 'tracking' section sends by: by hand, with the delay it would have in auto-progress.
    private static readonly TimeSpan DefaultTrackingDelay = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions SeedJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public SeedFilePersonaDirectory(IOptions<SeedOptions> options)
        : this(File.OpenRead(options.Value.Path))
    {
    }

    internal SeedFilePersonaDirectory(Stream seedFile)
    {
        using (seedFile)
        {
            var seed = JsonSerializer.Deserialize<SeedFile>(seedFile, SeedJson) ?? throw new InvalidDataException("The seed file is empty.");
            All = [.. seed.Personas.Select(persona => ToPersona(persona, seed))];
        }
    }

    public IReadOnlyList<Persona> All { get; }

    public Persona? Find(PersonaId id) => All.FirstOrDefault(persona => persona.Id == id);

    private static Persona ToPersona(SeedPersona persona, SeedFile seed)
    {
        IReadOnlyList<SeedAddress>? addresses = persona.Party.Type switch
        {
            PartyType.Shipper => seed.Shippers.FirstOrDefault(shipper => shipper.Id == persona.Party.Id) is { } shipper ? shipper.Addresses ?? [] : null,
            PartyType.Carrier => seed.Carriers.Any(carrier => carrier.Id == persona.Party.Id) ? [] : null,
            _ => null,
        };

        return new Persona(
            new PersonaId(persona.Id),
            persona.DisplayName,
            persona.Party.Type,
            persona.Party.Id,
            addresses?.Select(ToSavedAddress).ToArray()
                ?? throw new InvalidDataException($"Persona '{persona.Id}' plays {persona.Party.Type} {persona.Party.Id}, which the seed file doesn't have."),
            ToCarrierProfile(persona),
            ToShipperProfile(persona));
    }

    // A Carrier Persona must say how it answers and what its references start with, and a Shipper Persona has no use for either.
    private static CarrierProfile? ToCarrierProfile(SeedPersona persona)
    {
        if (persona.Party.Type != PartyType.Carrier)
        {
            return null;
        }

        if (persona.Carrier is not { } carrier)
        {
            throw new InvalidDataException($"Carrier Persona '{persona.Id}' needs a 'carrier' section with its booking reference prefix and answer mode.");
        }

        if (string.IsNullOrWhiteSpace(carrier.BookingReferencePrefix))
        {
            throw new InvalidDataException($"Carrier Persona '{persona.Id}' needs a booking reference prefix.");
        }

        return new CarrierProfile(
            carrier.BookingReferencePrefix,
            new CarrierAnswerPolicy(carrier.Answer.Mode, carrier.Answer.RejectionReason, TimeSpan.FromSeconds(carrier.Answer.DelaySeconds)),
            carrier.Tracking is { } tracking
                ? new CarrierTrackingPolicy(tracking.Mode, TimeSpan.FromSeconds(tracking.DelaySeconds))
                : new CarrierTrackingPolicy(TrackingMode.Manual, DefaultTrackingDelay));
    }

    // A Shipper Persona may say how often its tracking screen asks Norvia again; one that doesn't polls every few seconds.
    private static ShipperProfile? ToShipperProfile(SeedPersona persona)
    {
        if (persona.Party.Type != PartyType.Shipper || persona.Shipper?.TrackingPollSeconds is not { } seconds)
        {
            return null;
        }

        var interval = TimeSpan.FromSeconds(seconds);
        return interval >= ShipperProfile.MinimumTrackingPollInterval
            ? new ShipperProfile(interval)
            : throw new InvalidDataException($"Shipper Persona '{persona.Id}' polls every {seconds} seconds; the least is {ShipperProfile.MinimumTrackingPollInterval.TotalSeconds}.");
    }

    private static SavedAddress ToSavedAddress(SeedAddress address) =>
        new(address.Label, address.Street, address.Postcode, address.City, address.Country,
            address.Consignee is { } consignee ? new SavedConsignee(consignee.Name, consignee.ContactName, consignee.Phone) : null);

    private sealed record SeedFile(IReadOnlyList<SeedShipper> Shippers, IReadOnlyList<SeedCarrier> Carriers, IReadOnlyList<SeedPersona> Personas);

    private sealed record SeedShipper(string Id, IReadOnlyList<SeedAddress>? Addresses);

    private sealed record SeedCarrier(string Id);

    private sealed record SeedPersona(string Id, string DisplayName, SeedParty Party, SeedCarrierPersona? Carrier, SeedShipperPersona? Shipper);

    private sealed record SeedShipperPersona(double? TrackingPollSeconds);

    private sealed record SeedCarrierPersona(string BookingReferencePrefix, SeedAnswer Answer, SeedTracking? Tracking);

    private sealed record SeedTracking(TrackingMode Mode, double DelaySeconds);

    private sealed record SeedAnswer(CarrierAnswerMode Mode, CarrierRejectionReason RejectionReason, double DelaySeconds);

    private sealed record SeedParty(PartyType Type, string Id);

    private sealed record SeedAddress(string Label, string Street, string Postcode, string City, string Country, SeedConsignee? Consignee);

    private sealed record SeedConsignee(string Name, string? ContactName, string? Phone);
}
