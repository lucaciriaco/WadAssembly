using System.Text;
using System.Text.RegularExpressions;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Core.Maps;

/// <summary>
/// Parsed MAPINFO data for a single map.
/// </summary>
public sealed record MapInfoData(string? LevelName = null, string? MusicName = null);

/// <summary>
/// Parses MAPINFO/ZMAPINFO lumps to extract level names and music per map header.
/// Supports classic format: `map MAP01 "Level Name"` + `music D_RUNNIN`
/// and ZDoom format with key=value properties.
/// </summary>
public static class MapInfoParser
{
    /// <summary>
    /// Parses a MAPINFO/ZMAPINFO lump and returns a dictionary mapping
    /// map header names (e.g., "MAP01", "E1M1") to their level names and music.
    /// </summary>
    public static IReadOnlyDictionary<string, MapInfoData> Parse(WadFile wad)
    {
        var result = new Dictionary<string, MapInfoData>(StringComparer.OrdinalIgnoreCase);

        var mapInfoLump = wad.FindFirst("MAPINFO") ?? wad.FindFirst("ZMAPINFO");
        if (mapInfoLump is null)
            return result;

        string text = Encoding.UTF8.GetString(mapInfoLump.ReadAll());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? currentMap = null;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            // Skip comments
            if (line.StartsWith("//") || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            // Map definition: map MAP01 "Name" or map MAP01 { ... }
            if (line.StartsWith("map ", StringComparison.OrdinalIgnoreCase))
            {
                // Classic: map MAP01 "Name"
                var match = Regex.Match(line, @"^map\s+(\S+)\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    currentMap = match.Groups[1].Value;
                    string levelName = match.Groups[2].Value;
                    if (!result.TryGetValue(currentMap, out var existing))
                        existing = new MapInfoData();
                    result[currentMap] = existing with { LevelName = levelName };
                    continue;
                }

                // ZDoom namespace: map MAP01 { levelname = "Name"; music = "D_RUNNIN"; ... }
                var nsMatch = Regex.Match(line, @"^map\s+(\S+)\s*\{", RegexOptions.IgnoreCase);
                if (nsMatch.Success)
                {
                    currentMap = nsMatch.Groups[1].Value;
                    continue;
                }

                currentMap = null;
                continue;
            }

            // Inside a namespace block: levelname = "Name" or music = "D_RUNNIN"
            if (currentMap is not null)
            {
                // levelname = "Name"
                if (line.StartsWith("levelname", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"levelname\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        if (!result.TryGetValue(currentMap, out var existing))
                            existing = new MapInfoData();
                        result[currentMap] = existing with { LevelName = match.Groups[1].Value };
                        continue;
                    }
                }

                // music = "D_RUNNIN" (ZDoom) or music D_RUNNIN (classic)
                if (line.StartsWith("music", StringComparison.OrdinalIgnoreCase))
                {
                    // Try key=value format: music = "D_RUNNIN"
                    var kvMatch = Regex.Match(line, @"music\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (kvMatch.Success)
                    {
                        if (!result.TryGetValue(currentMap, out var existing))
                            existing = new MapInfoData();
                        result[currentMap] = existing with { MusicName = kvMatch.Groups[1].Value };
                        continue;
                    }

                    // Classic format: music D_RUNNIN (no =, no quotes)
                    var classicMatch = Regex.Match(line, @"^music\s+(\S+)", RegexOptions.IgnoreCase);
                    if (classicMatch.Success)
                    {
                        if (!result.TryGetValue(currentMap, out var existing))
                            existing = new MapInfoData();
                        result[currentMap] = existing with { MusicName = classicMatch.Groups[1].Value };
                        continue;
                    }
                }

                // End of namespace block
                if (line == "}")
                {
                    currentMap = null;
                }
            }
        }

        return result;
    }
}