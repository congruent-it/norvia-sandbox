using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet;

/// <param name="Licences">The Driver's licence categories, lowest first.</param>
/// <param name="CanDrive">The Vehicle types their highest licence covers.</param>
public sealed record DriverView(string Id, string Name, IReadOnlyList<LicenceCategory> Licences, IReadOnlyList<VehicleType> CanDrive);
