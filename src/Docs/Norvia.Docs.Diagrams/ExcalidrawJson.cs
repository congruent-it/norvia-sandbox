using System.Globalization;
using System.Text;

namespace Norvia.Docs.Diagrams;

/// <summary>
/// Writes the Excalidraw document as JSON, indented by two, with every non-ASCII character escaped, as the generators always have:
/// the checked-in files are the contract, so the text is the same, byte for byte.
/// </summary>
internal static class ExcalidrawJson
{
    public static string Write(object? value)
    {
        var sb = new StringBuilder();
        Append(sb, value, 0);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, object? value, int level)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case string s:
                AppendString(sb, s);
                break;
            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case Num n:
                sb.Append(n.ToString());
                break;
            case IReadOnlyDictionary<string, object?> map:
                AppendMap(sb, map, level);
                break;
            case System.Collections.IEnumerable list:
                AppendList(sb, list.Cast<object?>().ToList(), level);
                break;
            default:
                throw new NotSupportedException($"A drawing cannot hold a {value.GetType().Name}.");
        }
    }

    private static void AppendMap(StringBuilder sb, IReadOnlyDictionary<string, object?> map, int level)
    {
        if (map.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append('{');
        var first = true;
        foreach (var (key, item) in map)
        {
            sb.Append(first ? "\n" : ",\n").Append(' ', (level + 1) * 2);
            AppendString(sb, key);
            sb.Append(": ");
            Append(sb, item, level + 1);
            first = false;
        }

        sb.Append('\n').Append(' ', level * 2).Append('}');
    }

    private static void AppendList(StringBuilder sb, List<object?> list, int level)
    {
        if (list.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        sb.Append('[');
        var first = true;
        foreach (var item in list)
        {
            sb.Append(first ? "\n" : ",\n").Append(' ', (level + 1) * 2);
            Append(sb, item, level + 1);
            first = false;
        }

        sb.Append('\n').Append(' ', level * 2).Append(']');
    }

    private static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c is >= ' ' and <= '~')
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
