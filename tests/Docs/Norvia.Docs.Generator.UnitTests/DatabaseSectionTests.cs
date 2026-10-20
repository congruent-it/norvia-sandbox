namespace Norvia.Docs.Generator.UnitTests;

public sealed class DatabaseSectionTests
{
    private static readonly string Section = DatabaseSection.Render(SampleDatabase.Model(), ApplicationDescriptions.None);

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

        var section = DatabaseSection.Render(SampleDatabase.Model(), descriptions);

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
    public void Render_TheDiagram_DrawsKeyColumnsAndEachKindOfRelationship()
    {
        Section.Should().Contain("```mermaid\nerDiagram\n");
        Section.Should().Contain("    Customers {\n        bigint Id PK\n    }");
        Section.Should().Contain("    Orders {\n        int Id PK\n        bigint CustomerId FK\n    }");
        Section.Should().Contain("    Customers ||--o{ Orders : \"CustomerId\"");
        Section.Should().Contain("    Orders |o--o{ OrderNotes : \"OrderId\"");
        Section.Should().Contain("    Orders ||--o| Receipts : \"OrderId\"");
    }

    [Fact]
    public void Render_Twice_GivesTheSameText()
    {
        DatabaseSection.Render(SampleDatabase.Model(), ApplicationDescriptions.None).Should().Be(Section);
    }
}
