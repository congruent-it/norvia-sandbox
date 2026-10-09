using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class LicenceRulesTests
{
    [Theory]
    [InlineData(VehicleType.Van, LicenceCategory.B)]
    [InlineData(VehicleType.RigidTruck, LicenceCategory.C)]
    [InlineData(VehicleType.TractorWithTrailer, LicenceCategory.CE)]
    public void Needed_AVehicleType_IsItsLicenceCategory(VehicleType type, LicenceCategory expected)
    {
        LicenceRules.Needed(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(LicenceCategory.CE, LicenceCategory.CE, true)]
    [InlineData(LicenceCategory.CE, LicenceCategory.C, true)]
    [InlineData(LicenceCategory.CE, LicenceCategory.B, true)]
    [InlineData(LicenceCategory.C, LicenceCategory.C, true)]
    [InlineData(LicenceCategory.C, LicenceCategory.B, true)]
    [InlineData(LicenceCategory.C, LicenceCategory.CE, false)]
    [InlineData(LicenceCategory.B, LicenceCategory.B, true)]
    [InlineData(LicenceCategory.B, LicenceCategory.C, false)]
    [InlineData(LicenceCategory.B, LicenceCategory.CE, false)]
    public void Covers_AHeldCategory_CoversItselfAndTheLowerOnes(LicenceCategory held, LicenceCategory needed, bool covers)
    {
        LicenceRules.Covers(held, needed).Should().Be(covers);
    }

    [Theory]
    [InlineData(new[] { LicenceCategory.B }, VehicleType.Van, true)]
    [InlineData(new[] { LicenceCategory.B }, VehicleType.RigidTruck, false)]
    [InlineData(new[] { LicenceCategory.C }, VehicleType.Van, true)]
    [InlineData(new[] { LicenceCategory.C }, VehicleType.RigidTruck, true)]
    [InlineData(new[] { LicenceCategory.C }, VehicleType.TractorWithTrailer, false)]
    [InlineData(new[] { LicenceCategory.CE }, VehicleType.TractorWithTrailer, true)]
    [InlineData(new[] { LicenceCategory.B, LicenceCategory.C }, VehicleType.RigidTruck, true)]
    [InlineData(new[] { LicenceCategory.B, LicenceCategory.C, LicenceCategory.CE }, VehicleType.TractorWithTrailer, true)]
    [InlineData(new LicenceCategory[0], VehicleType.Van, false)]
    public void CanDrive_ADriver_WhenTheirHighestLicenceCoversTheVehicleType(LicenceCategory[] licences, VehicleType type, bool canDrive)
    {
        var driver = new Driver(new DriverId("DRV-0001"), "Test Driver", licences.ToHashSet(), Active: true);

        driver.CanDrive(type).Should().Be(canDrive);
    }
}
