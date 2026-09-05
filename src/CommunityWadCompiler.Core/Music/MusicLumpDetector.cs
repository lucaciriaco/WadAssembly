using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Core.Music;

/// <summary>A detected music lump with its final (possibly renamed) output name.</summary>
public sealed record MusicCandidate(string WadPath, string OriginalName, string FinalName);

/// <summary>
/// Detects music lumps (MUS format / MIDI "MThd") inside a WAD. Only lumps outside
/// map ranges are considered, mirroring how music is conventionally stored.
/// </summary>
public static class MusicLumpDetector
{
    private const int SignatureBytes = 4;

    /// <summary>True when the given bytes begin with a MUS signature or a MIDI MThd header.</summary>
    public static bool IsMusicData(byte[] data)
    {
        if (data.Length < SignatureBytes)
            return false;
        return IsMus(data) || IsMidi(data);
    }

    private static bool IsMus(byte[] d) =>
        d[0] == (byte)'M' && d[1] == (byte)'U' && d[2] == (byte)'S' && d[3] == 0x1A;

    private static bool IsMidi(byte[] d) =>
        d[0] == (byte)'M' && d[1] == (byte)'T' && d[2] == (byte)'h' && d[3] == (byte)'d';

    /// <summary>Names of the music lumps found outside the map ranges, in directory order.</summary>
    public static IReadOnlyList<string> DetectNames(WadFile wad)
    {
        var names = new List<string>();
        var ranges = MapDetector.DetectMaps(wad)
            .Select(m => (m.StartIndex, m.EndIndex))
            .ToList();

        for (int i = 0; i < wad.Lumps.Count; i++)
        {
            if (ranges.Any(r => i >= r.Item1 && i < r.Item2))
                continue;
            if (IsMusicData(wad.Lumps[i].ReadPrefix(SignatureBytes)))
                names.Add(wad.Lumps[i].Name);
        }

        return names;
    }

    /// <summary>
    /// Collects the music lumps of every WAD (in the given order, first source wins for
    /// the base name). When two WADs carry a lump with the same name, the later ones are
    /// renamed to <c>NOMBRE_WAD</c> (8-char lump-name limit enforced).
    /// </summary>
    public static IReadOnlyList<MusicCandidate> CollectAcrossWads(IEnumerable<WadFile> wads)
    {
        var candidates = new List<MusicCandidate>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wad in wads)
        {
            string? source = wad.SourcePath;
            foreach (string raw in DetectNames(wad))
            {
                string final = used.Contains(raw)
                    ? RenameDuplicate(raw, source, used)
                    : raw;
                used.Add(final);
                candidates.Add(new MusicCandidate(source ?? "", raw, final));
            }
        }
        return candidates;
    }

    /// <summary>
    /// Builds a free, 8-char-compliant name for a duplicate music lump: <c>NOMBRE_WAD</c>,
    /// shortening the base/tag as needed so the lump name never exceeds 8 characters.
    /// </summary>
    public static string RenameDuplicate(string original, string? wadPath, ISet<string> used)
    {
        string tag = wadPath is null ? "" : Path.GetFileNameWithoutExtension(wadPath).ToUpperInvariant();

        string joined = $"{original}_{tag}";
        if (joined.Length <= 8)
            return joined;

        for (int tagLen = Math.Min(tag.Length, 5); tagLen >= 1; tagLen--)
        {
            int baseRoom = 8 - tagLen - 1;
            if (baseRoom < 1)
                continue;
            string basePart = original.Length > baseRoom ? original[..baseRoom] : original;
            string candidate = $"{basePart}_{tag[..tagLen]}";
            if (!used.Contains(candidate))
                return candidate;
        }

        return $"{original[..Math.Min(original.Length, 7)]}2";
    }
}