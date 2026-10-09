using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

public sealed record VehicleView(string Id, string Registration, VehicleType Type, int PayloadKg, int PalletPlaces, LicenceCategory LicenceNeeded);
