using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.RateCards;

/// <summary>One dated version of a replaced Rate Card or Carrier Rate Card as the ERP's event spells it, in plain values.</summary>
public sealed record RateCardVersionData(int Version, DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<ZoneData> Zones, IReadOnlyList<LaneData> Lanes);
