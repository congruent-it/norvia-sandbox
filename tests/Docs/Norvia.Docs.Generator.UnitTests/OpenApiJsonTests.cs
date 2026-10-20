namespace Norvia.Docs.Generator.UnitTests;

public sealed class OpenApiJsonTests
{
    private const string Raw = """{"openapi":"3.1.1","info":{"title":"Host | v1","version":"1"},"servers":[{"url":"http://localhost/"}],"paths":{"/v1/a":{"get":{"summary":"It's <b>"}}}}""";

    [Fact]
    public void Normalize_ADocument_IsIndentedWithLfAndOneFinalNewline()
    {
        var json = OpenApiJson.Normalize(Raw, "Norvia.Tms.Api", "v1");

        json.Should().StartWith("{\n  \"openapi\": \"3.1.1\",\n");
        json.Should().EndWith("}\n");
        json.Should().NotContain("\r");
    }

    [Fact]
    public void Normalize_ADocument_NamesTheApiNotTheHost()
    {
        OpenApiJson.Normalize(Raw, "Norvia.Tms.Api", "v1").Should().Contain("\"title\": \"Norvia.Tms.Api | v1\"").And.NotContain("Host");
    }

    [Fact]
    public void Normalize_ADocument_DropsTheTestServer()
    {
        OpenApiJson.Normalize(Raw, "Norvia.Tms.Api", "v1").Should().NotContain("servers").And.NotContain("localhost");
    }

    [Fact]
    public void Normalize_Text_IsNotEscapedMoreThanJsonNeeds()
    {
        OpenApiJson.Normalize(Raw, "Norvia.Tms.Api", "v1").Should().Contain("\"summary\": \"It's <b>\"");
    }

    [Fact]
    public void Normalize_ADocumentWithoutInfo_IsKeptAsItIs()
    {
        OpenApiJson.Normalize("""{"openapi":"3.1.1"}""", "Norvia.Tms.Api", "v1").Should().Be("{\n  \"openapi\": \"3.1.1\"\n}\n");
    }

    [Fact]
    public void Normalize_NullJson_Throws()
    {
        var normalize = () => OpenApiJson.Normalize("null", "Norvia.Tms.Api", "v1");

        normalize.Should().Throw<InvalidOperationException>();
    }
}
