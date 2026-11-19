namespace Norvia.Docs.Diagrams.UnitTests;

public sealed class DiagramOutputTests
{
    private static readonly MatrixSpec Tiny = new()
    {
        Title = "Tiny",
        Subtitle = "s",
        Footnote = "f",
        Lanes = [new MatrixLane("a", "blue", "A", "sub")],
        Bands = [new MatrixBand("Stream", "blue", [new("Col", "P1")])],
        Steps = [new MatrixStep("s1", 0, "a", 0, "auto", "Step", "P1")],
    };

    [Fact]
    public void RenderAll_NoNames_DrawsEverySpecWithItsPrefix()
    {
        using var folder = new TempFolder();
        var log = new StringWriter();
        var specs = new Dictionary<string, MatrixSpec> { ["one"] = Tiny, ["two"] = Tiny };

        DiagramOutput.RenderAll(specs, folder.Directory, "p-", [], MatrixDiagram.Render, log);

        File.Exists(folder.Path("p-one.excalidraw")).Should().BeTrue();
        File.Exists(folder.Path("p-two.excalidraw")).Should().BeTrue();
        log.ToString().Should().Contain("wrote p-one.excalidraw").And.Contain("wrote p-two.excalidraw");
    }

    [Fact]
    public void RenderAll_WithNames_DrawsOnlyThose()
    {
        using var folder = new TempFolder();
        var specs = new Dictionary<string, MatrixSpec> { ["one"] = Tiny, ["two"] = Tiny };

        DiagramOutput.RenderAll(specs, folder.Directory, "", ["two"], MatrixDiagram.Render, new StringWriter());

        File.Exists(folder.Path("two.excalidraw")).Should().BeTrue();
        File.Exists(folder.Path("one.excalidraw")).Should().BeFalse();
    }

    [Fact]
    public void ToPng_WithoutRsvgConvert_SaysSoAndCarriesOn()
    {
        using var folder = new TempFolder();
        var log = new StringWriter();
        var path = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", folder.Directory);
        try
        {
            DiagramOutput.ToPng("in.svg", "out.png", log);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
        }

        log.ToString().Should().Contain("rsvg-convert not found");
    }

    [Fact]
    public void ToPng_ATool_RunsItWithTheSvgAndThePngPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var folder = new TempFolder();
        var tool = folder.Path("rsvg-convert");
        File.WriteAllText(tool, "#!/bin/sh\nprintf '%s ' \"$@\" > \"$5.args\"\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", folder.Directory);
        try
        {
            DiagramOutput.ToPng("in.svg", folder.Path("out.png"), new StringWriter());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
        }

        File.ReadAllText(folder.Path("out.png.args")).Trim().Should().Be($"-z 1 in.svg -o {folder.Path("out.png")}");
    }

    [Fact]
    public void ToPng_AToolThatFails_Throws()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var folder = new TempFolder();
        var tool = folder.Path("rsvg-convert");
        File.WriteAllText(tool, "#!/bin/sh\nexit 3\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", folder.Directory);
        try
        {
            var act = () => DiagramOutput.ToPng("in.svg", "out.png", new StringWriter());

            act.Should().Throw<InvalidOperationException>().WithMessage("*exit code 3*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
        }
    }

    [Fact]
    public void SpecDirectory_OutsideAFileBasedApp_Throws()
    {
        var act = DiagramOutput.SpecDirectory;

        act.Should().Throw<InvalidOperationException>();
    }
}
