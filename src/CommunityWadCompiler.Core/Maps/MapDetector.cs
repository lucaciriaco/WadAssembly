using System.Text.RegularExpressions;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Core.Maps;

/// <summary>
/// Detects map lump sequences inside a WAD:
///   - Classic Doom 1 (E1M1..E4M9)
///   - Classic Doom 2 / Hexen / Boom-MBF (MAP01..MAP99)
///   - UDMF (TEXTMAP/ENDMAP pair) and Doom-format maps with tertiary markers
/// </summary>
public static class MapDetector
{
    private static readonly Regex Doom1Header = new(@"^E[1-4]M[1-9]$", RegexOptions.Compiled);
    private static readonly Regex Doom2Header = new(@"^MAP\d{2,3}$", RegexOptions.Compiled);

    /// <summary>
    /// Lump names that form the body of a classic (non-UDMF) map.
    /// A marker followed by any of these is treated as a map start.
    /// </summary>
    private static readonly HashSet<string> MapBodyLumps = new(StringComparer.Ordinal)
    {
        "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS",
        "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "TEXTMAP", "ENDMAP",
    };

    /// <summary>True when the name can be a classic Doom map header (E1M1..E4M9, MAP01..).</summary>
    public static bool IsMapHeader(string name) => Doom1Header.IsMatch(name) || Doom2Header.IsMatch(name);

    /// <summary>
    /// Scans the WAD and returns every detected map in file order.
    /// </summary>
    public static IReadOnlyList<DetectedMap> DetectMaps(WadFile wad)
    {
        var result = new List<DetectedMap>();
        var lumps = wad.Lumps;

        for (int i = 0; i < lumps.Count; i++)
        {
            string name = lumps[i].Name;

            // A map marker is a recognized header, or any name immediately followed by TEXTMAP
            // (some editors allow exotic UDMF markers).
            bool isHeader = IsMapHeader(name);
            bool isUdmfMarker = !isHeader && i + 1 < lumps.Count && lumps[i + 1].Name == "TEXTMAP";
            if (!isHeader && !isUdmfMarker)
                continue;

            bool udmf = i + 1 < lumps.Count && lumps[i + 1].Name == "TEXTMAP";
            int end = i + 1;

            if (udmf || (end < lumps.Count && MapBodyLumps.Contains(lumps[end].Name)))
            {
                // Extend until the next map marker or, for classic maps, until the known
                // terminator lumps / a foreign lump.
                while (end + 1 < lumps.Count && !IsMapHeader(lumps[end + 1].Name))
                {
                    string next = lumps[end + 1].Name;
                    if (next == "TEXTMAP" && end + 2 < lumps.Count && IsMapHeader(lumps[end + 2].Name))
                        break; // stray TEXTMAP not belonging to a marker
                    if (!udmf && !MapBodyLumps.Contains(next))
                        break; // classic map ran into a foreign lump
                    end++;
                }

                result.Add(new DetectedMap(wad, i, end + 1, udmf));
                i = end; // skip over the map body
            }
        }

        return result;
    }
}