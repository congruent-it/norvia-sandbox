namespace Norvia.PartnerPortal.Domain.Personas;

/// <summary>An address in a party's own address book, with the Consignee there when goods are delivered to it.</summary>
/// <param name="Country">An ISO 3166-1 alpha-2 code, which the Norvia Partner API accepts as it is.</param>
public sealed record SavedAddress(string Label, string Street, string Postcode, string City, string Country, SavedConsignee? Consignee);
