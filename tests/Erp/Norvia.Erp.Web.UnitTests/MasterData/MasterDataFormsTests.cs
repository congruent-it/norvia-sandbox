using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Erp.Application.MasterData.Carriers.List;
using Norvia.Erp.Application.MasterData.Drivers;
using Norvia.Erp.Application.MasterData.Drivers.List;
using Norvia.Erp.Application.MasterData.RateCards;
using Norvia.Erp.Application.MasterData.RateCards.List;
using Norvia.Erp.Application.MasterData.RateCards.ListCarrier;
using Norvia.Erp.Application.MasterData.RateCards.Read;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Application.MasterData.Vehicles;
using Norvia.Erp.Application.MasterData.Vehicles.List;
using Norvia.Erp.Web.ErpApi;
using Norvia.Erp.Web.MasterData;

#pragma warning disable xUnit1051 // The forms pass their own token to the ERP API; the tests match any.

namespace Norvia.Erp.Web.UnitTests.MasterData;

/// <summary>Finance's create and edit forms for the six master-data entities, as Finance meets them: what is refused before it is sent, and what the ERP API's answer says.</summary>
public sealed class MasterDataFormsTests : ErpWebContext
{
    private static readonly RateCardVersionData Version = new(1, new DateOnly(2026, 1, 1), null, [new ZoneData("NL-30", "NL", ["30"])], []);

    private static readonly string[] HeldLicences = ["C", "CE"];

    private readonly IMasterDataCommands _commands = Substitute.For<IMasterDataCommands>();

    public MasterDataFormsTests()
    {
        Services.AddSingleton(_commands);
        Answer<ListShippersQuery, IReadOnlyList<ShipperView>>([new ShipperView("SHP-0001", "Brouwer Dranken", true, 3)]);
        Answer<ListCarriersQuery, IReadOnlyList<CarrierView>>([new CarrierView("CAR-0001", "Van Dijk Transport", false, 1)]);
        Answer<ListVehiclesQuery, IReadOnlyList<VehicleView>>([new VehicleView("VEH-0001", "AB-123-C", "RigidTruck", 9000, 18, true, 2)]);
        Answer<ListDriversQuery, IReadOnlyList<DriverView>>([new DriverView("DRV-0001", "Jan Jansen", ["B", "C"], true, 1)]);
        Answer<ListRateCardsQuery, IReadOnlyList<RateCardView>>([new RateCardView("RC-0001", "SHP-0001", "Brouwer Dranken", [])]);
        Answer<ListCarrierRateCardsQuery, IReadOnlyList<RateCardView>>([new RateCardView("CRC-0001", "CAR-0001", "Van Dijk Transport", [])]);
        Answer<GetRateCardQuery, RateCardRecord?>(new RateCardRecord("RC-0001", "SHP-0001", 2, [Version]));
        Answer<GetCarrierRateCardQuery, CarrierRateCardRecord?>(new CarrierRateCardRecord("CRC-0001", "CAR-0001", 1, [Version]));
        var saved = Task.FromResult<MasterDataAnswer>(new MasterDataAnswer.Saved(MasterDataOutcome.Updated, 4));
        _commands.SaveShipperAsync(default!, default!, default, default).ReturnsForAnyArgs(saved);
        _commands.SaveCarrierAsync(default!, default!, default, default).ReturnsForAnyArgs(saved);
        _commands.SaveVehicleAsync(default!, default!, default!, default, default, default, default).ReturnsForAnyArgs(saved);
        _commands.SaveDriverAsync(default!, default!, default!, default, default).ReturnsForAnyArgs(saved);
        _commands.SaveRateCardAsync(default!, default!, default!, default).ReturnsForAnyArgs(saved);
        _commands.SaveCarrierRateCardAsync(default!, default!, default!, default).ReturnsForAnyArgs(saved);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static void Type(IRenderedComponent<IComponent> page, string tag, int index, string value) => page.FindAll(tag)[index].Change(value);

    private static Task Tick(IRenderedComponent<IComponent> page, int index, bool ticked) =>
        page.InvokeAsync(() => page.FindComponents<FluentCheckbox>()[index].Instance.ValueChanged.InvokeAsync(ticked));

    private static void Save(IRenderedComponent<IComponent> page) => page.Find("fluent-button").Click();

    private static string Alert(IRenderedComponent<IComponent> page) => page.Find("[role=alert]").TextContent.Trim();

    [Fact]
    public void NewShipper_WithAnIDThatIsTaken_IsRefusedBeforeItIsSent()
    {
        var page = Render<ShipperFormPage>();
        Type(page, "fluent-text-field", 0, "shp-0001");
        Type(page, "fluent-text-field", 1, "Another");

        Save(page);

        Alert(page).Should().Be("There is already a Shipper with ID shp-0001.");
        _commands.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Name", "The Shipper needs an ID.")]
    [InlineData("SHP-0009", " ", "The Shipper needs a name.")]
    public void NewShipper_WithoutWhatItNeeds_IsRefusedBeforeItIsSent(string id, string name, string problem)
    {
        var page = Render<ShipperFormPage>();
        Type(page, "fluent-text-field", 0, id);
        Type(page, "fluent-text-field", 1, name);

        Save(page);

        Alert(page).Should().Be(problem);
        _commands.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void NewShipper_WithATooLongName_IsRefusedBeforeItIsSent()
    {
        var page = Render<ShipperFormPage>();
        Type(page, "fluent-text-field", 0, "SHP-0009");
        Type(page, "fluent-text-field", 1, new string('x', 201));

        Save(page);

        Alert(page).Should().Be("The name is at most 200 characters.");
    }

    [Fact]
    public void NewShipper_WithATooLongID_IsRefusedBeforeItIsSent()
    {
        var page = Render<ShipperFormPage>();
        Type(page, "fluent-text-field", 0, new string('x', 51));
        Type(page, "fluent-text-field", 1, "Name");

        Save(page);

        Alert(page).Should().Be("The ID is at most 50 characters.");
    }

    [Fact]
    public async Task NewShipper_IsSentActive_AndFinanceLandsOnItsEditPage()
    {
        _commands.SaveShipperAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromResult<MasterDataAnswer>(new MasterDataAnswer.Saved(MasterDataOutcome.Created, 1)));
        var page = Render<ShipperFormPage>();
        Type(page, "fluent-text-field", 0, " SHP-0009 ");
        Type(page, "fluent-text-field", 1, " Nieuw ");

        Save(page);

        await _commands.Received(1).SaveShipperAsync("SHP-0009", "Nieuw", true, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/shippers/SHP-0009/edit"));
    }

    [Fact]
    public async Task EditShipper_ShowsWhatItIs_KeepsItsID_AndSaysTheRevisionSaved()
    {
        var page = Render<ShipperFormPage>(parameters => parameters.Add(p => p.Id, "SHP-0001"));
        page.Markup.Should().Contain("Edit Shipper SHP-0001");
        page.FindAll("fluent-text-field")[0].HasAttribute("disabled").Should().BeTrue();
        page.FindAll("fluent-text-field")[1].GetAttribute("value").Should().Be("Brouwer Dranken");
        Type(page, "fluent-text-field", 1, "Brouwer BV");

        Save(page);

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Saved at Master-data revision 4"));
        await _commands.Received(1).SaveShipperAsync("SHP-0001", "Brouwer BV", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditShipper_WhenNothingChanged_SaysSo()
    {
        _commands.SaveShipperAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromResult<MasterDataAnswer>(new MasterDataAnswer.Saved(MasterDataOutcome.Unchanged, 3)));
        var page = Render<ShipperFormPage>(parameters => parameters.Add(p => p.Id, "SHP-0001"));

        Save(page);

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Nothing changed").And.Contain("revision 3"));
    }

    [Fact]
    public void EditShipper_WhenTheErpApiRefuses_ShowsItsReason()
    {
        _commands.SaveShipperAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromResult<MasterDataAnswer>(new MasterDataAnswer.Refused("The Shipper can't be saved.")));
        var page = Render<ShipperFormPage>(parameters => parameters.Add(p => p.Id, "SHP-0001"));

        Save(page);

        page.WaitForAssertion(() => Alert(page).Should().Be("The Shipper can't be saved."));
    }

    [Fact]
    public void EditShipper_WhenTheErpApiIsDown_SaysNothingWasSaved()
    {
        _commands.SaveShipperAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromException<MasterDataAnswer>(new HttpRequestException("down")));
        var page = Render<ShipperFormPage>(parameters => parameters.Add(p => p.Id, "SHP-0001"));

        Save(page);

        page.WaitForAssertion(() => Alert(page).Should().Be("The ERP API couldn't save the Shipper. Nothing was saved; try again."));
    }

    [Fact]
    public void EditShipper_ThatDoesNotExist_SaysSo()
    {
        var page = Render<ShipperFormPage>(parameters => parameters.Add(p => p.Id, "SHP-9999"));

        page.Markup.Should().Contain("There is no Shipper with ID SHP-9999.");
        page.FindAll("fluent-button").Should().BeEmpty();
    }

    [Fact]
    public void NewShipper_WhenTheShippersCantBeRead_SaysSo()
    {
        Fail<ListShippersQuery, IReadOnlyList<ShipperView>>(new InvalidOperationException("db"));

        var page = Render<ShipperFormPage>();

        page.Markup.Should().Contain("couldn't be read");
    }

    [Fact]
    public async Task EditCarrier_SendsTheActiveFlagFinanceChose()
    {
        var page = Render<CarrierFormPage>(parameters => parameters.Add(p => p.Id, "CAR-0001"));
        await Tick(page, 0, true);

        Save(page);

        await _commands.Received(1).SaveCarrierAsync("CAR-0001", "Van Dijk Transport", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NewCarrier_WithATakenID_IsRefused()
    {
        var page = Render<CarrierFormPage>();
        Type(page, "fluent-text-field", 0, "CAR-0001");
        Type(page, "fluent-text-field", 1, "Another");

        Save(page);

        Alert(page).Should().Be("There is already a Carrier with ID CAR-0001.");
    }

    [Fact]
    public void NewCarrier_WhenTheCarriersCantBeRead_SaysSo()
    {
        Fail<ListCarriersQuery, IReadOnlyList<CarrierView>>(new InvalidOperationException("db"));

        Render<CarrierFormPage>().Markup.Should().Contain("couldn't be read");
    }

    [Fact]
    public async Task NewVehicle_IsSentWithItsRegistrationTypePayloadAndPalletPlaces()
    {
        var page = Render<VehicleFormPage>();
        Type(page, "fluent-text-field", 0, "VEH-0009");
        Type(page, "fluent-text-field", 1, "ZZ-999-Z");
        page.Find("fluent-number-field").Change("1500");

        Save(page);

        await _commands.Received(1).SaveVehicleAsync("VEH-0009", "ZZ-999-Z", "Van", 1500, 0, true, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/vehicles/VEH-0009/edit"));
    }

    [Fact]
    public void NewVehicle_WithARegistrationAnotherVehicleHas_IsRefused()
    {
        var page = Render<VehicleFormPage>();
        Type(page, "fluent-text-field", 0, "VEH-0009");
        Type(page, "fluent-text-field", 1, "ab-123-c");

        Save(page);

        Alert(page).Should().Be("Another Vehicle already has the registration ab-123-c.");
        _commands.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("VEH-0009", "", "The Vehicle needs a registration.")]
    [InlineData("VEH-0001", "ZZ-999-Z", "There is already a Vehicle with ID VEH-0001.")]
    [InlineData("", "ZZ-999-Z", "The Vehicle needs an ID.")]
    public void NewVehicle_WithoutWhatItNeeds_IsRefused(string id, string registration, string problem)
    {
        var page = Render<VehicleFormPage>();
        Type(page, "fluent-text-field", 0, id);
        Type(page, "fluent-text-field", 1, registration);

        Save(page);

        Alert(page).Should().Be(problem);
    }

    [Fact]
    public void NewVehicle_WithATooLongRegistration_IsRefused()
    {
        var page = Render<VehicleFormPage>();
        Type(page, "fluent-text-field", 0, "VEH-0009");
        Type(page, "fluent-text-field", 1, new string('Z', 51));

        Save(page);

        Alert(page).Should().Be("The registration is at most 50 characters.");
    }

    [Theory]
    [InlineData("0", "10", "The payload is at least 1 kg.")]
    [InlineData("1000", "-1", "The pallet places can't be negative.")]
    public void NewVehicle_WithAnInvalidPayloadOrPalletPlaces_IsRefused(string payload, string pallets, string problem)
    {
        var page = Render<VehicleFormPage>();
        Type(page, "fluent-text-field", 0, "VEH-0009");
        Type(page, "fluent-text-field", 1, "ZZ-999-Z");
        page.FindAll("fluent-number-field")[0].Change(payload);
        page.FindAll("fluent-number-field")[1].Change(pallets);

        Save(page);

        Alert(page).Should().Be(problem);
    }

    [Fact]
    public async Task EditVehicle_KeepsItsOwnRegistration_AndSendsWhatItIs()
    {
        var page = Render<VehicleFormPage>(parameters => parameters.Add(p => p.Id, "VEH-0001"));
        page.FindAll("fluent-text-field")[1].GetAttribute("value").Should().Be("AB-123-C");

        Save(page);

        await _commands.Received(1).SaveVehicleAsync("VEH-0001", "AB-123-C", "RigidTruck", 9000, 18, true, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("revision 4"));
    }

    [Fact]
    public void EditVehicle_WhenTheErpApiIsDown_SaysNothingWasSaved()
    {
        _commands.SaveVehicleAsync(default!, default!, default!, default, default, default, default).ReturnsForAnyArgs(Task.FromException<MasterDataAnswer>(new HttpRequestException("down")));
        var page = Render<VehicleFormPage>(parameters => parameters.Add(p => p.Id, "VEH-0001"));

        Save(page);

        page.WaitForAssertion(() => Alert(page).Should().Contain("Nothing was saved"));
    }

    [Fact]
    public void EditVehicle_ThatDoesNotExist_SaysSo()
    {
        Render<VehicleFormPage>(parameters => parameters.Add(p => p.Id, "VEH-9999")).Markup.Should().Contain("There is no Vehicle with ID VEH-9999.");
    }

    [Fact]
    public void NewVehicle_WhenTheVehiclesCantBeRead_SaysSo()
    {
        Fail<ListVehiclesQuery, IReadOnlyList<VehicleView>>(new InvalidOperationException("db"));

        Render<VehicleFormPage>().Markup.Should().Contain("couldn't be read");
    }

    [Fact]
    public async Task EditDriver_ShowsTheLicencesHeld_AndSendsWhatFinanceTicks()
    {
        var page = Render<DriverFormPage>(parameters => parameters.Add(p => p.Id, "DRV-0001"));
        await Tick(page, 2, true); // CE
        await Tick(page, 0, false); // B

        Save(page);

        await _commands.Received(1).SaveDriverAsync("DRV-0001", "Jan Jansen", Arg.Is<IReadOnlyList<string>>(sent => sent.SequenceEqual(HeldLicences)), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewDriver_IsSentWithItsName_AndFinanceLandsOnItsEditPage()
    {
        var page = Render<DriverFormPage>();
        Type(page, "fluent-text-field", 0, "DRV-0009");
        Type(page, "fluent-text-field", 1, "Piet");

        Save(page);

        await _commands.Received(1).SaveDriverAsync("DRV-0009", "Piet", Arg.Is<IReadOnlyList<string>>(sent => sent.Count == 0), true, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/drivers/DRV-0009/edit"));
    }

    [Fact]
    public void NewDriver_WithATakenID_IsRefused()
    {
        var page = Render<DriverFormPage>();
        Type(page, "fluent-text-field", 0, "DRV-0001");
        Type(page, "fluent-text-field", 1, "Piet");

        Save(page);

        Alert(page).Should().Be("There is already a Driver with ID DRV-0001.");
    }

    [Fact]
    public void EditDriver_WhenTheErpApiIsDown_SaysNothingWasSaved()
    {
        _commands.SaveDriverAsync(default!, default!, default!, default, default).ReturnsForAnyArgs(Task.FromException<MasterDataAnswer>(new HttpRequestException("down")));
        var page = Render<DriverFormPage>(parameters => parameters.Add(p => p.Id, "DRV-0001"));

        Save(page);

        page.WaitForAssertion(() => Alert(page).Should().Contain("Nothing was saved"));
    }

    [Fact]
    public void EditDriver_ThatDoesNotExist_SaysSo()
    {
        Render<DriverFormPage>(parameters => parameters.Add(p => p.Id, "DRV-9999")).Markup.Should().Contain("There is no Driver with ID DRV-9999.");
    }

    [Fact]
    public void NewDriver_WhenTheDriversCantBeRead_SaysSo()
    {
        Fail<ListDriversQuery, IReadOnlyList<DriverView>>(new InvalidOperationException("db"));

        Render<DriverFormPage>().Markup.Should().Contain("couldn't be read");
    }

    [Fact]
    public void Vehicles_AreListedWithALinkToCreateAndEachOne()
    {
        var page = Render<VehiclesPage>();

        page.Markup.Should().Contain("VEH-0001").And.Contain("AB-123-C").And.Contain("RigidTruck");
        page.FindAll("a").Select(link => link.GetAttribute("href")).Should().Contain(["/vehicles/new", "/vehicles/VEH-0001/edit"]);
    }

    [Fact]
    public void Drivers_AreListedWithTheirLicences_AndALinkToCreateAndEachOne()
    {
        var page = Render<DriversPage>();

        page.Markup.Should().Contain("Jan Jansen").And.Contain("B, C");
        page.FindAll("a").Select(link => link.GetAttribute("href")).Should().Contain(["/drivers/new", "/drivers/DRV-0001/edit"]);
    }

    [Fact]
    public void VehiclesAndDrivers_WhenTheyCantBeRead_SayWhichAreMissing()
    {
        Fail<ListVehiclesQuery, IReadOnlyList<VehicleView>>(new InvalidOperationException("db"));
        Fail<ListDriversQuery, IReadOnlyList<DriverView>>(new InvalidOperationException("db"));

        Render<VehiclesPage>().Markup.Should().Contain("vehicles couldn't be read");
        Render<DriversPage>().Markup.Should().Contain("drivers couldn't be read");
    }

    [Fact]
    public void NewRateCard_WithVersionsThatAreNotJson_IsRefusedBeforeItIsSent()
    {
        var page = Render<RateCardFormPage>();
        Type(page, "fluent-text-field", 0, "RC-0009");
        page.Find("fluent-text-area").Change("{ not json");

        Save(page);

        Alert(page).Should().StartWith("The versions aren't valid:");
        _commands.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void NewRateCard_WithoutAVersion_IsRefused()
    {
        var page = Render<RateCardFormPage>();
        Type(page, "fluent-text-field", 0, "RC-0009");
        page.Find("fluent-text-area").Change("[]");

        Save(page);

        Alert(page).Should().Be("The Rate Card needs at least one version.");
    }

    [Fact]
    public void NewRateCard_WithATakenID_IsRefused()
    {
        var page = Render<RateCardFormPage>();
        Type(page, "fluent-text-field", 0, "RC-0001");

        Save(page);

        Alert(page).Should().Be("There is already a Rate Card with ID RC-0001.");
    }

    [Fact]
    public async Task NewRateCard_StartsFromAVersionFinanceFillsIn_AndIsSentForTheShipperChosen()
    {
        var page = Render<RateCardFormPage>();
        Type(page, "fluent-text-field", 0, "RC-0009");

        Save(page);

        await _commands.Received(1).SaveRateCardAsync("RC-0009", "SHP-0001", Arg.Is<IReadOnlyList<RateCardVersionData>>(versions => versions.Count == 1 && versions[0].Version == 1), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/rate-cards/RC-0009/edit"));
    }

    [Fact]
    public async Task EditRateCard_ShowsItsWholeListOfVersions_AndSendsThemBack()
    {
        var page = Render<RateCardFormPage>(parameters => parameters.Add(p => p.Id, "RC-0001"));
        page.Find("fluent-text-area").GetAttribute("current-value").Should().Contain("NL-30");

        Save(page);

        await _commands.Received(1).SaveRateCardAsync("RC-0001", "SHP-0001", Arg.Is<IReadOnlyList<RateCardVersionData>>(versions => versions.Count == 1 && versions[0].Zones[0].Name == "NL-30"), Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("revision 4"));
    }

    [Fact]
    public void EditRateCard_WhenTheErpApiIsDown_SaysNothingWasSaved()
    {
        _commands.SaveRateCardAsync(default!, default!, default!, default).ReturnsForAnyArgs(Task.FromException<MasterDataAnswer>(new HttpRequestException("down")));
        var page = Render<RateCardFormPage>(parameters => parameters.Add(p => p.Id, "RC-0001"));

        Save(page);

        page.WaitForAssertion(() => Alert(page).Should().Contain("Nothing was saved"));
    }

    [Fact]
    public void EditRateCard_ThatDoesNotExist_SaysSo()
    {
        Answer<GetRateCardQuery, RateCardRecord?>(null);

        Render<RateCardFormPage>(parameters => parameters.Add(p => p.Id, "RC-9999")).Markup.Should().Contain("There is no Rate Card with ID RC-9999.");
    }

    [Fact]
    public void NewRateCard_WhenTheRateCardsCantBeRead_SaysSo()
    {
        Fail<ListRateCardsQuery, IReadOnlyList<RateCardView>>(new InvalidOperationException("db"));

        Render<RateCardFormPage>().Markup.Should().Contain("couldn't be read");
    }

    [Fact]
    public async Task EditCarrierRateCard_SendsItsCarrier()
    {
        var page = Render<CarrierRateCardFormPage>(parameters => parameters.Add(p => p.Id, "CRC-0001"));
        page.Markup.Should().Contain("Edit Carrier Rate Card CRC-0001");

        Save(page);

        await _commands.Received(1).SaveCarrierRateCardAsync("CRC-0001", "CAR-0001", Arg.Any<IReadOnlyList<RateCardVersionData>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewCarrierRateCard_IsSentForTheCarrierChosen()
    {
        var page = Render<CarrierRateCardFormPage>();
        Type(page, "fluent-text-field", 0, "CRC-0009");

        Save(page);

        await _commands.Received(1).SaveCarrierRateCardAsync("CRC-0009", "CAR-0001", Arg.Any<IReadOnlyList<RateCardVersionData>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditCarrierRateCard_ThatDoesNotExist_SaysSo()
    {
        Answer<GetCarrierRateCardQuery, CarrierRateCardRecord?>(null);

        Render<CarrierRateCardFormPage>(parameters => parameters.Add(p => p.Id, "CRC-9999")).Markup.Should().Contain("There is no Carrier Rate Card with ID CRC-9999.");
    }

    [Fact]
    public void NewCarrierRateCard_WhenTheCardsCantBeRead_SaysSo()
    {
        Fail<ListCarrierRateCardsQuery, IReadOnlyList<RateCardView>>(new InvalidOperationException("db"));

        Render<CarrierRateCardFormPage>().Markup.Should().Contain("couldn't be read");
    }

    [Fact]
    public void RateCardLists_LinkEachCardToItsEditPage()
    {
        Answer<ListRateCardsQuery, IReadOnlyList<RateCardView>>([new RateCardView("RC-0001", "SHP-0001", "Brouwer Dranken", [new RateCardVersionView(1, new DateOnly(2026, 1, 1), null, 1, [])])]);

        Render<RateCardsPage>().FindAll("a").Select(link => link.GetAttribute("href")).Should().Contain("/rate-cards/RC-0001/edit");
    }
}
