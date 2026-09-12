using WadAssembly.Core.Maps;
using WadAssembly.Core.WadFormat;

namespace WadAssembly.Core.Music;

/// <summary>A detected music lump with its final (possibly renamed) output name.</summary>
public sealed record MusicCandidate(string WadPath, string OriginalName, string FinalName);

/// <summary>
/// Detects music lumps (MUS / MIDI "MThd" / Impulse Tracker "IMPM" / XM "Extended Module:" /
/// ProTracker MOD) inside a WAD. Only lumps outside map ranges are considered.
/// </summary>
public static class MusicLumpDetector
{
    private const int SignatureBytes = 4;
    private const int ModSignatureOffset = 1080; // MOD header signature, e.g. "M.K."
    private const int MusicHeaderBytes = ModSignatureOffset + SignatureBytes; // 1084

    /// <summary>
    /// True when the given bytes begin with a MUS/MIDI/IT/XM signature, or carry a known
    /// ProTracker-family MOD signature at offset 1080.
    /// </summary>
    public static bool IsMusicData(byte[] data)
    {
        if (data.Length < SignatureBytes)
            return false;
        if (IsMus(data) || IsMidi(data) || IsImpulseTracker(data) || IsXM(data))
            return true;
        return data.Length >= MusicHeaderBytes && IsModule(data);
    }

    /// <summary>True when the lump begins with a signature of any supported music format.</summary>
    public static bool IsMusicData(Lump lump)
        => IsMusicData(lump.ReadPrefix(MusicHeaderBytes));

    private static bool IsMus(byte[] d) =>
        d[0] == (byte)'M' && d[1] == (byte)'U' && d[2] == (byte)'S' && d[3] == 0x1A;

    private static bool IsMidi(byte[] d) =>
        d[0] == (byte)'M' && d[1] == (byte)'T' && d[2] == (byte)'h' && d[3] == (byte)'d';

    private static bool IsImpulseTracker(byte[] d) =>
        d[0] == (byte)'I' && d[1] == (byte)'M' && d[2] == (byte)'P' && d[3] == (byte)'M';

    private static bool IsXM(byte[] d) =>
        d.Length >= 17 &&
        d[0] == 'E' && d[1] == 'x' && d[2] == 't' && d[3] == 'e' &&
        d[4] == 'n' && d[5] == 'd' && d[6] == 'e' && d[7] == 'd' &&
        d[8] == ' ' && d[9] == 'M' && d[10] == 'o' && d[11] == 'd' &&
        d[12] == 'u' && d[13] == 'l' && d[14] == 'e' && d[15] == ':' &&
        d[16] == ' ';

    private static bool IsModule(byte[] d)
    {
        string signature = System.Text.Encoding.ASCII.GetString(d, ModSignatureOffset, SignatureBytes);
        return ModSignatures.Contains(signature);
    }

    /// <summary>Well-known 4-char signatures of the ProTracker/NoiseTracker family at offset 1080.</summary>
    private static readonly HashSet<string> ModSignatures = new(StringComparer.Ordinal)
    {
        "M.K.", "M!K!", "M&K!", "N.T.", "FLT4", "FLT8", "CD81", "OKTA",
        "4CHN", "5CHN", "6CHN", "7CHN", "8CHN", "9CHN", "10CH", "11CH", "12CH", "16CN",
        "20CH", "24CH", "28CH", "30CH", "31CH", "32CH", "EXO4", "EXO8",
        "PATT", "TDZ1", "TDZ2", "TDZ3", "M15M",
    };

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
            if (IsMusicData(wad.Lumps[i]))
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