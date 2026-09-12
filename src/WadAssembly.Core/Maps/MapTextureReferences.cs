using WadAssembly.Core.WadFormat;

namespace WadAssembly.Core.Maps;

/// <summary>
/// The set of wall textures and flats referenced by a map (or collection of maps).
/// </summary>
public sealed class UsedTextures
{
    /// <summary>Wall texture names referenced from sidedefs (TEXTURE1/TEXTURE2 or ZDoom TEXTURES).</summary>
    public HashSet<string> Walls { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Flat names referenced from sectors (floors/ceilings).</summary>
    public HashSet<string> Flats { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Merges a second map's usage into this one.</summary>
    public void UnionWith(UsedTextures other)
    {
        Walls.UnionWith(other.Walls);
        Flats.UnionWith(other.Flats);
    }
}

/// <summary>
/// Extracts the wall textures and flats used by a map by inspecting its geometry lumps:
/// classic maps via SIDEDEFS/SECTORS, UDMF maps via the TEXTMAP lump.
/// </summary>
public static class MapTextureAnalyzer
{
    /// <summary>Record size in bytes of a classic Doom SIDEDEFS lump.</summary>
    private const int SideDefSize = 30; // xoff, yoff, top[8], bottom[8], middle[8], sector

    private static readonly HashSet<string> NoTexture = new(StringComparer.Ordinal) { "-", "" };

    /// <summary>Collects every wall texture and flat name referenced by the map.</summary>
    public static UsedTextures Analyze(DetectedMap map)
    {
        var used = new UsedTextures();
        if (map.IsUdmf)
            AnalyzeUdmf(map, used);
        else
            AnalyzeClassic(map, used);
        return used;
    }

    private static void AnalyzeClassic(DetectedMap map, UsedTextures used)
    {
        Lump? sidedefs = FindInRange(map, "SIDEDEFS");
        if (sidedefs is not null)
        {
            byte[] data = sidedefs.ReadAll();
            for (int i = 0; i + SideDefSize <= data.Length; i += SideDefSize)
            {
                AddName(used.Walls, WadFile.ReadLumpName(data, i + 4));     // upper
                AddName(used.Walls, WadFile.ReadLumpName(data, i + 12));    // lower
                AddName(used.Walls, WadFile.ReadLumpName(data, i + 20));    // middle
            }
        }

        Lump? sectors = FindInRange(map, "SECTORS");
        if (sectors is not null)
        {
            byte[] data = sectors.ReadAll();
            (int stride, int offset) = DetectSectorLayout(data);
            for (int i = 0; i + stride <= data.Length; i += stride)
            {
                AddName(used.Flats, WadFile.ReadLumpName(data, i + offset));         // floor
                AddName(used.Flats, WadFile.ReadLumpName(data, i + offset + 8));     // ceiling
            }
        }
    }

    /// <summary>
    /// Picks the SECTORS record layout that parses cleanly. Classic Doom uses 22-byte
    /// records with the texture names at +0/+8; many community/exported maps use 26-byte
    /// records with the names at +4/+12 (extra leading/trailing fields). The best-scoring
    /// candidate is used so flat usage keeps being detected for both families.
    /// </summary>
    private static (int Stride, int Offset) DetectSectorLayout(byte[] data)
    {
        var candidates = new (int Stride, int Offset)[] { (22, 0), (26, 4), (20, 0), (24, 4) };

        (int Stride, int Offset) best = (22, 0);
        int bestValid = -1;
        foreach (var (stride, offset) in candidates)
        {
            int valid = 0;
            for (int i = 0; i + stride <= data.Length; i += stride)
            {
                if (IsNameField(data, i + offset) && IsNameField(data, i + offset + 8))
                    valid++;
            }
            if (valid > bestValid)
            {
                bestValid = valid;
                best = (stride, offset);
            }
        }

        // Fall back to the classic layout when nothing parses with reasonable coverage.
        return bestValid * 2 >= data.Length / best.Stride ? best : (22, 0);
    }

    private static bool IsNameField(byte[] data, int offset)
    {
        if (offset + 8 > data.Length)
            return false;
        bool seenNull = false;
        bool any = false;
        for (int j = 0; j < 8; j++)
        {
            byte b = data[offset + j];
            if (seenNull)
            {
                if (b != 0)
                    return false;
                continue;
            }
            if (b == 0)
            {
                seenNull = true;
                continue;
            }
            if (b < 0x20 || b > 0x7e)
                return false;
            any = true;
        }
        return any || seenNull;
    }

    private static void AnalyzeUdmf(DetectedMap map, UsedTextures used)
    {
        Lump? textmap = FindInRange(map, "TEXTMAP");
        if (textmap is null)
            return;

        string text = System.Text.Encoding.ASCII.GetString(textmap.ReadAll());
        foreach (string rawE in text.Split('\n'))
        {
            string line = rawE.Trim();
            if (System.String.IsNullOrEmpty(line) || line.StartsWith("//", System.StringComparison.Ordinal))
                continue;

            // Strip trailing ";".
            string decl = line.EndsWith(";", System.StringComparison.Ordinal) ? line[..^1] : line;

            int eq = decl.IndexOf('=');
            if (eq < 0)
                continue;

            string key = decl[..eq].Trim();
            string value = ExtractName(decl[(eq + 1)..].Trim());

            switch (key)
            {
                case "texturetop":
                case "texturebottom":
                case "texturemiddle":
                    AddName(used.Walls, value);
                    break;
                case "floorpic":
                case "ceilingpic":
                    AddName(used.Flats, value);
                    break;
            }
        }
    }

    /// <summary>Strips UDMF quoting from a string value ("WOOD" -> WOOD).</summary>
    private static string ExtractName(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static void AddName(HashSet<string> set, string name)
    {
        if (!NoTexture.Contains(name) && !string.IsNullOrWhiteSpace(name))
            set.Add(name);
    }

    private static Lump? FindInRange(DetectedMap map, string name)
    {
        var lumps = map.Wad.Lumps;
        for (int i = map.StartIndex + 1; i < map.EndIndex; i++)
            if (string.Equals(lumps[i].Name, name, System.StringComparison.Ordinal))
                return lumps[i];
        return null;
    }
}