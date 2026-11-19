using System.ComponentModel;
using System.Diagnostics;

namespace Norvia.Docs.Diagrams;

/// <summary>Where a generator writes, and how it turns an SVG preview into the PNG the docs embed.</summary>
public static class DiagramOutput
{
    /// <summary>The folder of the file-based app that is running: the diagrams are written beside their spec.</summary>
    public static string SpecDirectory()
    {
        var directory = AppContext.GetData("EntryPointFileDirectoryPath") as string;
        return directory ?? throw new InvalidOperationException("Run a generator with 'dotnet run <file>.cs', so it knows where its spec lives.");
    }

    /// <summary>
    /// Draws each spec to <c>&lt;outDir&gt;/&lt;prefix&gt;&lt;name&gt;.excalidraw</c> and <c>.png</c>; no names draws them all.
    /// The SVG preview goes to the temp folder; the PNG needs rsvg-convert.
    /// </summary>
    public static void RenderAll<TSpec>(IReadOnlyDictionary<string, TSpec> specs, string outDir, string prefix, IReadOnlyList<string> names, Action<TSpec, string, string?> render, TextWriter? log = null)
    {
        foreach (var name in names.Count > 0 ? names : [.. specs.Keys])
        {
            var excalidraw = Path.Combine(outDir, $"{prefix}{name}.excalidraw");
            var svg = Path.Combine(Path.GetTempPath(), $"{prefix}{name}.svg");
            render(specs[name], excalidraw, svg);
            ToPng(svg, Path.ChangeExtension(excalidraw, ".png"), log ?? Console.Out);
            (log ?? Console.Out).WriteLine($"wrote {Path.GetFileName(excalidraw)}");
        }
    }

    /// <summary>Turns an SVG into a PNG at its own size with rsvg-convert; says so, and carries on, when it is not installed.</summary>
    public static void ToPng(string svg, string png, TextWriter log)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("rsvg-convert") { ArgumentList = { "-z", "1", svg, "-o", png }, UseShellExecute = false })
                ?? throw new InvalidOperationException("rsvg-convert did not start.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"rsvg-convert failed with exit code {process.ExitCode} for {svg}.");
            }
        }
        catch (Win32Exception)
        {
            log.WriteLine("rsvg-convert not found: wrote the .excalidraw only");
        }
    }
}
