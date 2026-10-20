namespace Norvia.Docs.Generator.UnitTests;

public sealed class ApplicationDescriptionsTests
{
    private const string Json = """{ "tables": { "Shipments": { "description": "One shipment.", "columns": { "Id": "The number." } } } }""";

    [Fact]
    public void Parse_ADescriptionsFile_GivesTableAndColumnDescriptions()
    {
        var descriptions = ApplicationDescriptions.Parse(Json);

        descriptions.OfTable("Shipments").Should().Be("One shipment.");
        descriptions.OfColumn("Shipments", "Id").Should().Be("The number.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_NoFile_GivesNoDescriptions(string? json)
    {
        ApplicationDescriptions.Parse(json).Should().BeSameAs(ApplicationDescriptions.None);
    }

    [Fact]
    public void Of_AnUndescribedTableOrColumn_IsNull()
    {
        var descriptions = ApplicationDescriptions.Parse(Json);

        descriptions.OfTable("Trips").Should().BeNull();
        descriptions.OfColumn("Shipments", "Status").Should().BeNull();
        descriptions.OfColumn("Trips", "Id").Should().BeNull();
    }
}
