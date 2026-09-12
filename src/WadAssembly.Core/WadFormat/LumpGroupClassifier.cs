namespace WadAssembly.Core.WadFormat;

/// <summary>Category of a lump based on the marker group it lives in, mirroring the
/// marker conventions of classic Doom editors (same pairings used by the merger).</summary>
public enum LumpCategory
{
    /// <summary>Sprite graphics: inside S_START..S_END or SS_START..SS_END.</summary>
    Sprite,

    /// <summary>Flat graphics: inside F_START..F_END or FF_START..F_END.</summary>
    Flat,

    /// <summary>Wall texture patches: inside P_START..P_END, PP_START..PP_END or T_START..T_END.</summary>
    Texture,

    /// <summary>Any lump not enclosed by a known marker group (standalone patches, globals...).</summary>
    Other,
}

/// <summary>
/// Classifies lumps by the graphics group their markers place them in. Marker lumps
/// themselves are classified as their own group, and <see cref="IsMarker"/> lets callers
/// drop the markers from content lists.
/// </summary>
public static class LumpGroupClassifier
{
    private static readonly Dictionary<string, (string End, LumpCategory Category)> GroupStarts =
        new(StringComparer.Ordinal)
        {
            ["S_START"] = ("S_END", LumpCategory.Sprite),
            ["SS_START"] = ("SS_END", LumpCategory.Sprite),
            ["F_START"] = ("F_END", LumpCategory.Flat),
            ["FF_START"] = ("F_END", LumpCategory.Flat),
            ["P_START"] = ("P_END", LumpCategory.Texture),
            ["PP_START"] = ("PP_END", LumpCategory.Texture),
            ["T_START"] = ("T_END", LumpCategory.Texture),
        };

    private static readonly HashSet<string> MarkerNames = BuildMarkerNames();

    private static HashSet<string> BuildMarkerNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string start in GroupStarts.Keys)
            names.Add(start);
        foreach (var (end, _) in GroupStarts.Values)
            names.Add(end);
        names.Add("SS_END");
        return names;
    }

    /// <summary>True when the given lump name is an opening or closing marker.</summary>
    public static bool IsMarker(string name) => MarkerNames.Contains(name);

    /// <summary>One category per lump, aligned by index with <paramref name="lumps"/>.</summary>
    public static LumpCategory[] ClassifyAll(IReadOnlyList<Lump> lumps)
    {
        var result = new LumpCategory[lumps.Count];
        string? groupEnd = null;
        var groupCategory = LumpCategory.Other;

        for (int i = 0; i < lumps.Count; i++)
        {
            string name = lumps[i].Name;

            if (groupEnd is null && GroupStarts.TryGetValue(name, out var start))
            {
                groupEnd = start.End;
                groupCategory = start.Category;
                result[i] = start.Category;
                continue;
            }

            if (groupEnd is not null && name == groupEnd)
            {
                result[i] = groupCategory;
                groupEnd = null;
                continue;
            }

            result[i] = groupEnd is null ? LumpCategory.Other : groupCategory;
        }

        return result;
    }
}