using System.Diagnostics;
using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Textures;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// The merge pipeline: loads the input WADs, detects maps, merges textures/resources
/// and writes a single output PWAD.
///
/// Current capabilities:
///   - map detection + slot assignment (explicit or automatic)
///   - PNAMES/TEXTURE1/TEXTURE2 merging (classic Doom format)
///   - generic lump deduplication (first occurrence wins)
///   - base WAD (IWAD) resource skipping
///
/// Known limitations (tracked as TODOs upstream):
///   - ZDoom TEXTURES lump merging is NOT implemented yet
///   - Boom/graphic-lump list-style content (ANIMATED, SWITCHES, ANIMDEFS, SNDINFO,
///     UMAPINFO, MUSINFO, ...) uses "first source wins" instead of a real merge
/// </summary>
public sealed class WadMerger
{
    private readonly MergeResult _result = new();

    /// <summary>
    /// Lumps that are never copied verbatim; they are handled by dedicated merge logic.
    /// </summary>
    private static readonly HashSet<string> TextureLumpNames = new(StringComparer.Ordinal)
    {
        "PNAMES", "TEXTURE1", "TEXTURE2", "TEXTURES",
    };

    /// <summary>
    /// Single-lump, engine-scoped lumps where a real merge is not implemented yet;
    /// the first source providing each one wins. (TODO: implement proper concatenation
    /// for ANIMATED/SWITCHES and last-wins for text lumps.)
    /// </summary>
    private static readonly HashSet<string> SingleWinningLumps = new(StringComparer.Ordinal)
    {
        "ANIMDEFS", "SNDINFO", "SNDSEQ", "UMAPINFO", "MUSINFO", "MAPINFO", "ZMAPINFO",
        "GLDEFS", "DECORATE", "LANGUAGE", "KEYCONF", "CONSOLE", "ANIMATED", "SWITCHES",
    };

    /// <summary>
    /// Runs the full merge. Blocks; callers should run it on a worker thread and feed
    /// <paramref name="progress"/> to surface status to a UI.
    /// </summary>
    public MergeResult Merge(MergeRequest request, IProgress<string>? progress = null)
    {
        _result.Errors.Clear();
        _result.Warnings.Clear();
        _result.Info.Clear();
        var stopwatch = Stopwatch.StartNew();
        void Report(string message) => progress?.Report(message);
        void Warn(string m) => _result.Warnings.Add(m);

        try
        {
            Report("Abriendo WAD base...");
            WadFile? baseWad = LoadOptional(request.BaseWadPath, Report);

            Report("Abriendo WADs de entrada...");
            var inputs = new List<WadFile>(request.InputWadPaths.Count);
            foreach (string path in request.InputWadPaths)
                inputs.Add(WadFile.Open(path));

            Report("Detectando mapas...");
            var assignments = BuildAssignments(inputs, request, Warn);

            Report("Fusionando texturas (PNAMES/TEXTURE1/TEXTURE2)...");
            var textures = MergeTextures(baseWad, inputs, request.Options);

            Report("Ensamblando WAD de salida...");
            string outputPath = BuildOutput(request, inputs, assignments, textures, baseWad, Report, Warn);

            _result.OutputPath = outputPath;
            _result.OutputBytes = new FileInfo(outputPath).Length;
            _result.Success = true;
        }
        catch (Exception ex)
        {
            _result.Errors.Add(ex.Message);
            _result.Success = false;
        }
        finally
        {
            _result.Elapsed = stopwatch.Elapsed;
        }

        return _result;
    }

    private static WadFile? LoadOptional(string? path, Action<string> report)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            report("  (sin WAD base)");
            return null;
        }
        return WadFile.Open(path);
    }

    // ------------------------------------------------------------------
    // Map assignments
    // ------------------------------------------------------------------

    private static IReadOnlyList<(DetectedMap Map, string FinalName)> BuildAssignments(
        List<WadFile> inputs,
        MergeRequest request,
        Action<string> warn)
    {
        var explicitMap = request.MapAssignments
            .ToDictionary(a => (a.WadPath, a.OriginalMapName), a => a.FinalMapName, DefaultComparer.Instance);

        var result = new List<(DetectedMap, string)>();
        var seenFinals = new HashSet<string>(StringComparer.Ordinal);
        int autoCounter = 1;

        for (int w = 0; w < inputs.Count; w++)
        {
            var wad = inputs[w];
            foreach (var map in MapDetector.DetectMaps(wad))
            {
                string? final = null;
                string? wadPath = wad.SourcePath;
                if (wadPath is not null && explicitMap.TryGetValue((wadPath, map.OriginalName), out string? assigned))
                    final = assigned;
                else if (request.Options.AutoAssignMaps)
                    final = $"MAP{autoCounter++:D2}";

                if (final is null)
                {
                    warn($"Mapa '{map.OriginalName}' de '{wadPath}' no tiene slot asignado; se omite.");
                    continue;
                }

                if (!seenFinals.Add(final))
                {
                    throw new InvalidOperationException($"Dos mapas se asignaron al slot '{final}'. Revisá las asignaciones.");
                }

                result.Add((map, final));
            }
        }

        return result;
    }

    private sealed class DefaultComparer : IEqualityComparer<(string, string)>
    {
        public static readonly DefaultComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);
        public int GetHashCode((string, string) obj) => HashCode.Combine(obj.Item1.ToLowerInvariant(), obj.Item2);
    }

    // ------------------------------------------------------------------
    // Texture merging
    // ------------------------------------------------------------------

    private TextureMerger MergeTextures(WadFile? baseWad, List<WadFile> inputs, MergeOptions options)
    {
        var merger = new TextureMerger();

        if (baseWad is not null && options.IncludeBaseWadTextures)
            merger.MergeSource(baseWad);

        foreach (var wad in inputs)
            merger.MergeSource(wad);
        // TODO(avanzado): ZDoom "TEXTURES" (text) lump fusion. Today those lumps are
        // dropped from the output with a warning in BuildOutput.

        return merger;
    }

    // ------------------------------------------------------------------
    // Output assembly
    // ------------------------------------------------------------------

    private string BuildOutput(
        MergeRequest request,
        List<WadFile> inputs,
        IReadOnlyList<(DetectedMap Map, string FinalName)> assignments,
        TextureMerger textures,
        WadFile? baseWad,
        Action<string> report,
        Action<string> warn)
    {
        var builder = new WadBuilder();
        var outputNames = new HashSet<string>(StringComparer.Ordinal);

        // 1. Merged texture scaffold.
        if (textures.HasContent)
        {
            builder.AddLump("PNAMES", textures.BuildPnames());
            builder.AddLump("TEXTURE1", textures.BuildTexture1());
            outputNames.Add("PNAMES");
            outputNames.Add("TEXTURE1");
            _result.PatchesMerged = textures.PatchNames.Count;
            _result.TexturesMerged = textures.Textures.Count;
            _result.TexturesDuplicated = textures.SkippedTextures;
        }
        foreach (string t in textures.Warnings)
            warn(t);

        // 2. Non-map lumps from the inputs, deduplicated (first wins).
        var skipFromBase = BuildBaseSkipSet(baseWad, request.Options);
        bool zdoomTexturesSeen = false;

        foreach (var wad in inputs)
        {
            // Lump indices consumed by the maps of this wad.
            var mapRanges = MapDetector.DetectMaps(wad)
                .Select(m => (m.StartIndex, m.EndIndex))
                .ToList();

            for (int i = 0; i < wad.Lumps.Count; i++)
            {
                var lump = wad.Lumps[i];
                if (mapRanges.Any(r => i >= r.Item1 && i < r.Item2))
                    continue;

                if (TextureLumpNames.Contains(lump.Name))
                {
                    if (lump.Name == "TEXTURES")
                        zdoomTexturesSeen = true;
                    continue;
                }

                if (skipFromBase.Contains(lump.Name))
                    continue;

                if (SingleWinningLumps.Contains(lump.Name) && outputNames.Contains(lump.Name))
                    continue; // first source wins (TODO: real merge for these)

                if (outputNames.Contains(lump.Name))
                {
                    _result.DuplicatesSkipped++;
                    continue;
                }

                builder.AddLump(lump.Name, lump.ReadAll());
                outputNames.Add(lump.Name);
                _result.LumpsCopied++;
            }
        }

        if (zdoomTexturesSeen)
            warn("Se detectaron lumps ZDoom 'TEXTURES'; su fusión aún no está implementada y se omitieron.");

        // 3. Maps in slot order.
        foreach (var (map, finalName) in assignments)
        {
            report($"  Copiando {map.OriginalName} -> {finalName}");
            var lumps = map.Wad.Lumps;
            for (int i = map.StartIndex; i < map.EndIndex; i++)
            {
                string name = i == map.StartIndex ? finalName : lumps[i].Name;
                builder.AddLump(name, lumps[i].ReadAll());
            }
            _result.MapsAdded++;
        }

        // 4. Write.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath!))!);
        builder.Write(request.OutputPath!, request.Options.OutputType);
        return request.OutputPath!;
    }

    private static HashSet<string> BuildBaseSkipSet(WadFile? baseWad, MergeOptions options)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (baseWad is null || !options.SkipBaseWadResources)
            return set;

        foreach (var lump in baseWad.Lumps)
            if (!TextureLumpNames.Contains(lump.Name))
                set.Add(lump.Name);
        return set;
    }
}