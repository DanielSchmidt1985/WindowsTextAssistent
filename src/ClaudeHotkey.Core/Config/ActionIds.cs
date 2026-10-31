using System.Text;

namespace ClaudeHotkey.Core.Config;

/// <summary>Creates the internal id of a new action from its label, e.g. "Höflicher" → "hoeflicher".</summary>
public static class ActionIds
{
    public static string FromLabel(string label, IEnumerable<string> existingIds)
    {
        var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var baseId = Slug(label);
        if (baseId.Length == 0)
            baseId = "aktion";

        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    internal static string Slug(string label)
    {
        var builder = new StringBuilder();
        foreach (var c in label.Trim().ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': builder.Append("ae"); break;
                case 'ö': builder.Append("oe"); break;
                case 'ü': builder.Append("ue"); break;
                case 'ß': builder.Append("ss"); break;
                case >= 'a' and <= 'z' or >= '0' and <= '9': builder.Append(c); break;
                default:
                    if (builder.Length > 0 && builder[^1] != '-')
                        builder.Append('-');
                    break;
            }
        }
        return builder.ToString().Trim('-');
    }
}
