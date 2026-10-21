using System.Text.Json;

namespace Norvia.Docs.Generator.UnitTests;

public sealed class EntityDiagramTests
{
    private static readonly JsonElement Diagram = JsonDocument.Parse(EntityDiagram.Json(SampleDatabase.Model())).RootElement;

    private static JsonElement Entity(string name) => Diagram.GetProperty("entities").EnumerateArray().Single(entity => entity.GetProperty("name").GetString() == name);

    private static JsonElement Relationship(string principal, string dependent) =>
        Diagram.GetProperty("relationships").EnumerateArray().Single(r => r.GetProperty("principal").GetString() == principal && r.GetProperty("dependent").GetString() == dependent);

    [Fact]
    public void Json_AnEntity_ListsOnlyItsKeyColumnsWithTheirKeys()
    {
        var columns = Entity("Orders").GetProperty("columns").EnumerateArray().Select(column => $"{column.GetProperty("name").GetString()}:{string.Join('+', column.GetProperty("keys").EnumerateArray().Select(key => key.GetString()))}");

        columns.Should().Equal("Id:PK", "CustomerId:FK");
    }

    [Fact]
    public void Json_EachKindOfRelationship_SaysWhetherItIsOptionalAndOneToOne()
    {
        Relationship("Customers", "Orders").GetProperty("optional").GetBoolean().Should().BeFalse();
        Relationship("Customers", "Orders").GetProperty("oneToOne").GetBoolean().Should().BeFalse();
        Relationship("Orders", "OrderNotes").GetProperty("optional").GetBoolean().Should().BeTrue();
        Relationship("Orders", "Receipts").GetProperty("oneToOne").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Json_Twice_GivesTheSameText()
    {
        EntityDiagram.Json(SampleDatabase.Model()).Should().Be(EntityDiagram.Json(SampleDatabase.Model()));
    }
}
