using System.Text;
using System.Text.RegularExpressions;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Core.Maps;

/// <summary>
/// Parsed MAPINFO data for a single map.
/// </summary>
public sealed record MapInfoData(string? LevelName = null, string? MusicName = null, string? SkyName = null);

/// <summary>
/// Parses MAPINFO/ZMAPINFO lumps to extract level names, music and sky per map header.
/// Accepts classic format (`map MAP01 "Name"` + top-level properties), the ZDoom/GZDoom
/// brace format and UMAPINFO. Braces may open on the map line (`map MAP01 { ... }`),
/// on the next line (`map MAP01` then `{ ... }`), with or without a quoted level name,
/// and values may be quoted or unquoted (`music = "D_RUNNIN"` vs `music D_RUNNIN`).
/// </summary>
public static class MapInfoParser
{
    /// <summary>
    /// Parses a MAPINFO/ZMAPINFO lump and returns a dictionary mapping
    /// map header names (e.g., "MAP01", "E1M1") to their level names, music and sky.
    /// </summary>
    public static IReadOnlyDictionary<string, MapInfoData> Parse(WadFile wad)
    {
        var mapInfoLump = wad.FindFirst("MAPINFO") ?? wad.FindFirst("ZMAPINFO");
        if (mapInfoLump is null)
            return new Dictionary<string, MapInfoData>(StringComparer.OrdinalIgnoreCase);

        return Parse(Encoding.UTF8.GetString(mapInfoLump.ReadAll()));
    }

    /// <summary>
    /// Parses MAPINFO text in classic, ZDoom/GZDoom and UMAPINFO syntax (braces on the map
    /// line or on their own line) and returns per-map header info.
    /// </summary>
    public static IReadOnlyDictionary<string, MapInfoData> Parse(string text)
    {
        var result = new Dictionary<string, MapInfoData>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? currentMap = null;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            if (line.Length == 0)
                continue;
            if (line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith("#", StringComparison.Ordinal) ||
                line.StartsWith(";", StringComparison.Ordinal))
                continue;

            // Map definition. Accepted forms:
            //   map MAP01 "Level name"
            //   map MAP01 "Level name" { ... }     (braces open on the same line)
            //   map MAP01 { ... }
            //   map MAP01                            (braces open next line, or classic props below)
            if (line.StartsWith("map ", StringComparison.OrdinalIgnoreCase))
            {
                var headerMatch = Regex.Match(line, @"^map\s+(\S+)", RegexOptions.IgnoreCase);
                if (!headerMatch.Success)
                    continue;

                currentMap = headerMatch.Groups[1].Value;

                var nameMatch = Regex.Match(line, @"""([^""]+)""");
                if (nameMatch.Success)
                    SetData(result, currentMap, new MapInfoData(LevelName: nameMatch.Groups[1].Value));

                // Braces may open on the same line; process the inline block statements.
                int open = line.IndexOf('{');
                if (open >= 0)
                    ProcessChunk(result, ref currentMap, line[(open + 1)..]);
                continue;
            }

            if (currentMap is null)
                continue;

            ProcessChunk(result, ref currentMap, line);
        }

        return result;
    }

    /// <summary>
    /// Processes one line/chunk maintaining the current map block: a closing brace ends it,
    /// any statements before/after braces are parsed (multiple statements separated by ';').
    /// </summary>
    private static void ProcessChunk(
        Dictionary<string, MapInfoData> result, ref string? currentMap, string chunk)
    {
        // Closing brace on this chunk terminates the block; the statements before it still count.
        int close = chunk.IndexOf('}');
        if (close >= 0)
        {
            foreach (string stmt in chunk[..close].Split(';'))
                ApplyStatement(result, currentMap, stmt);
            currentMap = null;
            return;
        }

        // An opening brace on this chunk just marks the block start; REST of the line may hold
        // statements (UMAPINFO inline) and the block itself keeps going afterwards.
        int open = chunk.IndexOf('{');
        if (open >= 0)
        {
            foreach (string stmt in chunk[(open + 1)..].Split(';'))
                ApplyStatement(result, currentMap, stmt);
            return;
        }

        foreach (string stmt in chunk.Split(';'))
            ApplyStatement(result, currentMap, stmt);
    }

    /// <summary>
    /// Parses a single `key = value` or `key value` statement and stores it in the current map.
    /// </summary>
    private static void ApplyStatement(
        Dictionary<string, MapInfoData> result, string? currentMap, string statement)
    {
        if (currentMap is null)
            return;

        statement = statement.Trim();
        if (statement.Length == 0)
            return;

        if (TryReadValue(statement, "levelname", out string? lvl))
        {
            if (!result.TryGetValue(currentMap, out var existing))
                existing = new MapInfoData();
            result[currentMap] = existing with { LevelName = lvl };
            return;
        }

        if (TryReadValue(statement, "music", out string? music))
        {
            if (!result.TryGetValue(currentMap, out var existing))
                existing = new MapInfoData();
            result[currentMap] = existing with { MusicName = music };
            return;
        }

        if (TryReadValue(statement, "sky1", out string? sky))
        {
            if (!result.TryGetValue(currentMap, out var existing))
                existing = new MapInfoData();
            result[currentMap] = existing with { SkyName = sky };
        }
    }

    /// <summary>
    /// Reads the value of a key that may appear as `key = "value"`, `key = value` or `key value`
    /// (mix of classic, ZDoom and UMAPINFO flavors).
    /// </summary>
    private static bool TryReadValue(string statement, string key, out string? value)
    {
        value = null;
        var match = Regex.Match(
            statement,
            $@"^{Regex.Escape(key)}(?:\s*=\s*|\s+)(?:""([^""]+)""|(\S+))",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;
        value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return true;
    }

    private static void SetData(Dictionary<string, MapInfoData> result, string map, MapInfoData data)
    {
        if (!result.TryGetValue(map, out var existing))
            existing = new MapInfoData();
        result[map] = existing with
        {
            LevelName = data.LevelName ?? existing.LevelName,
            MusicName = data.MusicName ?? existing.MusicName,
            SkyName = data.SkyName ?? existing.SkyName,
        };
    }
}