namespace Norvia.Docs.Generator.UnitTests;

public sealed class DatabaseSectionTests
{
    private static readonly string Section = DatabaseSection.Render(SampleDatabase.Model(), ApplicationDescriptions.None, "sample");

    [Fact]
    public void Render_ATable_ListsItsColumnsWithTypeNullabilityDefaultAndKey()
    {
        Section.Should().Contain("#### `Customers`");
        Section.Should().Contain("| `Id` | `bigint` | no | identity | PK |  |");
        Section.Should().Contain("| `Name` | `nvarchar(200)` | no |  |  |  |");
        Section.Should().Contain("| `Notes` | `nvarchar(max)` | yes |  |  |  |");
    }

    [Fact]
    public void Render_AColumnWithADefault_ShowsTheDefault()
    {
        Section.Should().Contain("| `Status` | `nvarchar(20)` | no | 'New' |  |  |");
    }

    [Fact]
    public void Render_ADescribedTableAndColumn_ShowsBoth()
    {
        var descriptions = ApplicationDescriptions.Parse("""
            { "tables": { "Customers": { "description": "Who orders.", "columns": { "Name": "The trading name | legal name" } } } }
            """);

        var section = DatabaseSection.Render(SampleDatabase.Model(), descriptions, "sample");

        section.Should().Contain("#### `Customers`\n\nWho orders.\n");
        section.Should().Contain("| `Name` | `nvarchar(200)` | no |  |  | The trading name \\| legal name |");
    }

    [Fact]
    public void Render_APrimaryKey_NamesItAndItsColumns()
    {
        Section.Should().Contain("Primary key `PK_Customers`: `Id`.");
    }

    [Fact]
    public void Render_ForeignKeys_ShowColumnsTargetAndDeleteBehaviour()
    {
        Section.Should().Contain("| `FK_Orders_Customers_CustomerId` | `CustomerId` | `Customers` (`Id`) | cascade |");
        Section.Should().Contain("| `FK_OrderNotes_Orders_OrderId` | `OrderId` | `Orders` (`Id`) | set null |");
        Section.Should().Contain("| `FK_Receipts_Orders_OrderId` | `OrderId` | `Orders` (`Id`) | restrict |");
    }

    [Fact]
    public void Render_ANullableForeignKeyColumn_IsMarkedAsAForeignKey()
    {
        Section.Should().Contain("| `OrderId` | `int` | yes |  | FK |  |");
    }

    [Fact]
    public void Render_AColumnThatIsBothPrimaryAndForeignKey_IsMarkedAsBoth()
    {
        Section.Should().Contain("| `OrderId` | `int` | no |  | PK, FK |  |");
    }

    [Fact]
    public void Render_UniqueConstraintsAndIndexes_AreListed()
    {
        Section.Should().Contain("| `AK_Orders_Reference` | `Reference` |");
        Section.Should().Contain("| `IX_Customers_Name` | `Name` | yes |  |");
        Section.Should().Contain("| `IX_OrderNotes_OrderId` | `OrderId` | no | `[OrderId] IS NOT NULL` |");
    }

    [Fact]
    public void Render_ACheckConstraint_IsListed()
    {
        Section.Should().Contain("| `CK_Orders_Total` | `[Total] >= 0` |");
    }

    [Fact]
    public void Render_ASequence_IsListedWithItsStartAndIncrement()
    {
        Section.Should().Contain("| `OrderNumbers` | `bigint` | 1000 | 5 |");
    }

    [Fact]
    public void Render_TheDiagram_EmbedsThePictureAndLinksItsSources()
    {
        Section.Should().Contain("![sample entity relationships](../architecture/diagrams/er-sample.png)");
        Section.Should().Contain("(../architecture/diagrams/er-sample.json)");
        Section.Should().Contain("(../architecture/diagrams/er-sample.excalidraw)");
    }

    [Fact]
    public void Render_Twice_GivesTheSameText()
    {
        DatabaseSection.Render(SampleDatabase.Model(), ApplicationDescriptions.None, "sample").Should().Be(Section);
    }
}
