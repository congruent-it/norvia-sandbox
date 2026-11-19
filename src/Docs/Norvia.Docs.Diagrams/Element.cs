namespace Norvia.Docs.Diagrams;

/// <summary>One Excalidraw element: its properties in the order they are written.</summary>
public sealed class Element : Dictionary<string, object?>
{
    public string Id => (string)this["id"]!;

    public List<object?> BoundElements => (List<object?>)this["boundElements"]!;
}

/// <summary>A point of a drawing.</summary>
public readonly record struct Pt(Num X, Num Y)
{
    public static implicit operator Pt((Num X, Num Y) tuple) => new(tuple.X, tuple.Y);
}

/// <summary>A box: left, top, width, height.</summary>
public readonly record struct Box(Num X, Num Y, Num W, Num H);
