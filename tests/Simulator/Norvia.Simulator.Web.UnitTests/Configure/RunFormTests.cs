using System.ComponentModel.DataAnnotations;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Web.Configure;

namespace Norvia.Simulator.Web.UnitTests.Configure;

public sealed class RunFormTests
{
    private static List<ValidationResult> Validate(RunForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Validate_AFormWithinTheRangesAndCaps_HasNoProblem()
    {
        Validate(new RunForm { Limits = TestData.Limits }).Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(RunForm.Orders), 0)]
    [InlineData(nameof(RunForm.BadPercent), 101)]
    [InlineData(nameof(RunForm.TodayPercent), -1)]
    [InlineData(nameof(RunForm.WindowMinutes), 241)]
    [InlineData(nameof(RunForm.AssignedPercent), 101)]
    [InlineData(nameof(RunForm.OwnFleetPercent), -1)]
    [InlineData(nameof(RunForm.ReassignPercent), 101)]
    [InlineData(nameof(RunForm.ReleasePercent), 101)]
    [InlineData(nameof(RunForm.ConfirmPercent), -1)]
    [InlineData(nameof(RunForm.DeclinePercent), 101)]
    [InlineData(nameof(RunForm.TrackedPercent), -1)]
    [InlineData(nameof(RunForm.ExceptionPercent), 101)]
    [InlineData(nameof(RunForm.DeliveredPercent), -1)]
    [InlineData(nameof(RunForm.ProofOfDeliveryPercent), 101)]
    [InlineData(nameof(RunForm.CancelPercent), -1)]
    public void Validate_AFieldOutOfRange_IsNamedByItsField(string field, int value)
    {
        var form = new RunForm { Limits = TestData.Limits };
        typeof(RunForm).GetProperty(field)!.SetValue(form, value);

        Validate(form).Should().ContainSingle().Which.MemberNames.Should().Equal(field);
    }

    [Fact]
    public void Validate_MoreOrdersThanTheCap_IsAProblemOfTheWholeForm()
    {
        var problem = Validate(new RunForm { Orders = 51, Limits = TestData.Limits }).Should().ContainSingle().Subject;

        problem.ErrorMessage.Should().Contain("at most 50");
        problem.MemberNames.Should().BeEmpty();
    }

    [Fact]
    public void ToSettings_APacedForm_HasItsWindow_ABurstHasNone()
    {
        var form = new RunForm { Mode = nameof(RunMode.Paced), WindowMinutes = 15, Seed = 9 };
        form.ToSettings().Window.Should().Be(TimeSpan.FromMinutes(15));
        form.Mode = nameof(RunMode.Burst);

        form.ToSettings().Window.Should().Be(TimeSpan.Zero);
        form.ToSettings().PresetName.Should().BeNull();
    }

    [Fact]
    public void ToSettings_CarriesThePlannersShares()
    {
        var form = new RunForm { AssignedPercent = 70, OwnFleetPercent = 30, ReassignPercent = 10 };

        var settings = form.ToSettings();

        (settings.AssignedPercent, settings.OwnFleetPercent, settings.ReassignPercent).Should().Be((70, 30, 10));
    }

    [Fact]
    public void ToSettings_CarriesTheCarriersShares()
    {
        var form = new RunForm
        {
            ReleasePercent = 80, ConfirmPercent = 60, DeclinePercent = 20, TrackedPercent = 85, ExceptionPercent = 20, DeliveredPercent = 70, ProofOfDeliveryPercent = 80, CancelPercent = 40,
        };

        form.ToSettings().Carriers.Should().Be(new Norvia.Simulator.Domain.Carriers.CarrierShares(80, 60, 20, 85, 20, 70, 80, 40));
    }

    [Fact]
    public void ToSettings_CarriesTheShareOfOwnFleetShipmentsPutOnTrips()
    {
        new RunForm { TripPercent = 40 }.ToSettings().TripPercent.Should().Be(40);
    }

    [Fact]
    public void ToSettings_CarriesTheDriversShares()
    {
        var form = new RunForm { CollectionFailedPercent = 20, DeliveryFailedPercent = 25, DamagedPercent = 7, ReservationsPercent = 40 };

        form.ToSettings().Drivers.Should().Be(new Norvia.Simulator.Domain.Drivers.DriverShares(20, 25, 7, 40));
    }

    [Fact]
    public void Fill_APreset_FillsTheDriversShares()
    {
        var form = new RunForm();

        form.Fill(Norvia.Simulator.Domain.Runs.Presets.Find("Peak week")!);

        form.ToSettings().Drivers.Should().Be(new Norvia.Simulator.Domain.Drivers.DriverShares(15, 15, 10, 15));
    }

    [Fact]
    public void Validate_MoreFailuresThanShipments_IsAProblemOfTheWholeForm()
    {
        var problem = Validate(new RunForm { CollectionFailedPercent = 60, DeliveryFailedPercent = 50, Limits = TestData.Limits }).Should().ContainSingle().Subject;

        problem.ErrorMessage.Should().Contain("can't be more than all");
    }

    [Fact]
    public void Validate_MoreConfirmedAndDeclinedThanReleased_IsAProblemOfTheWholeForm()
    {
        var problem = Validate(new RunForm { ConfirmPercent = 70, DeclinePercent = 40, Limits = TestData.Limits }).Should().ContainSingle().Subject;

        problem.ErrorMessage.Should().Contain("confirm and decline more than all");
    }

    [Fact]
    public void Fill_APreset_TakesItsNumbersAndName()
    {
        var form = new RunForm();

        form.Fill(Presets.All[1]);

        form.ToSettings().Should().Be(Presets.All[1].ToSettings(0));
    }
}
