using System.Diagnostics;
using System.Text;
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
///   - optional resource WADs (texture/flat packs) with filter-to-used support
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
    /// Non-texture lumps always implemented from a resource WAD even when filtering to
    /// used resources, because they are shared graphics state (palette / animation tables).
    /// </summary>
    private static readonly HashSet<string> AlwaysCopyResourceLumps = new(StringComparer.Ordinal)
    {
        "PLAYPAL", "COLORMAP", "ANIMATED",
    };

    /// <summary>
    /// Marker group pairings used to delimit graphics sections in a WAD. The markers are
    /// implemented only when at least one lump of the group passes the resource filter, so
    /// vanilla-style engines and editors (Slade/GZDB) keep recognizing the grouped graphics
    /// as flats/patches.
    /// </summary>
    private static readonly Dictionary<string, string> MarkerEnds = new(StringComparer.Ordinal)
    {
        ["F_START"] = "F_END",
        ["FF_START"] = "F_END",
        ["P_START"] = "P_END",
        ["PP_START"] = "PP_END",
        ["T_START"] = "T_END",
        ["S_START"] = "S_END",
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

            Report("Abriendo WADs de recursos...");
            var resources = new List<WadFile>(request.ResourceWadPaths.Count);
            foreach (string path in request.ResourceWadPaths)
                resources.Add(WadFile.Open(path));
            if (resources.Count == 0)
                Report("  (sin WAD de recursos)");

            Report("Detectando mapas...");
            var assignments = BuildAssignments(inputs, request, Warn);

            // Which textures/flats do the merged maps need? Needed only when resource
            // WADs are in play and the user asked to filter to used resources.
            UsedTextures? usage = null;
            if (resources.Count > 0 && request.Options.FilterToUsedResources)
            {
                Report("Analizando texturas/flats usados por los mapas...");
                usage = AnalyzeUsage(assignments);
                Report($"  - {usage.Walls.Count} texturas de pared y {usage.Flats.Count} flats detectados.");
            }

            Report("Fusionando texturas (PNAMES/TEXTURE1/TEXTURE2)...");
            IReadOnlyList<WadFile> textureSources = resources.Count > 0 ? resources : inputs;
            var textures = MergeTextures(baseWad, textureSources, request.Options);
            if (usage is not null)
            {
                int excluded = textures.ApplyUsageFilter(usage.Walls);
                _result.TexturesExcluded = excluded;
                Report($"  {excluded} textura(s) excluidas por no usarse.");
            }

            Report("Ensamblando WAD de salida...");
            string outputPath = BuildOutput(
                request, inputs, resources, assignments, textures, usage, baseWad, Report, Warn);

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

    private static IReadOnlyList<(DetectedMap Map, string FinalName, string? LevelName)> BuildAssignments(
        List<WadFile> inputs,
        MergeRequest request,
        Action<string> warn)
    {
        var explicitMap = request.MapAssignments
            .ToDictionary(a => (a.WadPath, a.OriginalMapName), DefaultComparer.Instance);

        var result = new List<(DetectedMap, string, string?)>();
        var seenFinals = new HashSet<string>(StringComparer.Ordinal);
        int autoCounter = 1;

        for (int w = 0; w < inputs.Count; w++)
        {
            var wad = inputs[w];
            foreach (var map in MapDetector.DetectMaps(wad))
            {
                string? final = null;
                string? levelName = null;
                string? wadPath = wad.SourcePath;
                if (wadPath is not null && explicitMap.TryGetValue((wadPath, map.OriginalName), out MapAssignment? assignment))
                {
                    final = assignment.FinalMapName;
                    levelName = assignment.LevelName;
                }
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

                result.Add((map, final, levelName));
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

    /// <summary>Unions the wall texture and flat usage of every merged map.</summary>
    private static UsedTextures AnalyzeUsage(IReadOnlyList<(DetectedMap Map, string FinalName, string? LevelName)> assignments)
    {
        var used = new UsedTextures();
        foreach (var (map, _, _) in assignments)
            used.UnionWith(MapTextureAnalyzer.Analyze(map));
        return used;
    }

    // ------------------------------------------------------------------
    // Texture merging
    // ------------------------------------------------------------------

    private TextureMerger MergeTextures(WadFile? baseWad, IReadOnlyList<WadFile> textureSources, MergeOptions options)
    {
        var merger = new TextureMerger();

        if (baseWad is not null && options.IncludeBaseWadTextures)
            merger.MergeSource(baseWad);

        foreach (var wad in textureSources)
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
        List<WadFile> resources,
        IReadOnlyList<(DetectedMap Map, string FinalName, string? LevelName)> assignments,
        TextureMerger textures,
        UsedTextures? usage,
        WadFile? baseWad,
        Action<string> report,
        Action<string> warn)
    {
        var builder = new WadBuilder();
        var outputNames = new HashSet<string>(StringComparer.Ordinal);

        // 0. Generated MAPINFO (level names) comes first.
        (string? mapInfoText, int mapInfoCount) = request.Options.GenerateMapInfo
            ? BuildMapInfo(assignments)
            : (null, 0);
        bool mapInfoSeen = false;
        if (mapInfoText is not null)
        {
            builder.AddLump("MAPINFO", Encoding.UTF8.GetBytes(mapInfoText));
            outputNames.Add("MAPINFO");
            _result.Info.Add($"MAPINFO generado para {mapInfoCount} mapa(s).");
        }

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
            CopyGenericLumps(wad, builder, outputNames, skipFromBase, null, textures,
                ref zdoomTexturesSeen, mapInfoText is not null, ref mapInfoSeen, warn);

        foreach (var wad in resources)
            CopyGenericLumps(wad, builder, outputNames, skipFromBase, usage, textures,
                ref zdoomTexturesSeen, mapInfoText is not null, ref mapInfoSeen, warn);

        if (mapInfoText is not null && mapInfoSeen)
            warn("Se omitieron lumps MAPINFO/ZMAPINFO existentes en los WADs; se usa el MAPINFO generado con los nombres.");

        if (zdoomTexturesSeen)
            warn("Se detectaron lumps ZDoom 'TEXTURES'; su fusión aún no está implementada y se omitieron.");

        // 3. Maps in slot order.
        foreach (var (map, finalName, _) in assignments)
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

    /// <summary>
    /// Copies non-map lumps of one WAD into the output, deduplicating by name
    /// (first occurrence wins). When <paramref name="usage"/> is provided the WAD is a
    /// resource WAD filtered to used resources: only flats/patches the maps need
    /// (plus palette-graphical lumps) are implemented.
    /// </summary>
    private void CopyGenericLumps(
        WadFile wad,
        WadBuilder builder,
        HashSet<string> outputNames,
        HashSet<string> skipFromBase,
        UsedTextures? usage,
        TextureMerger textures,
        ref bool zdoomTexturesSeen,
        bool skipMapInfo,
        ref bool mapInfoSeen,
        Action<string> warn)
    {
        var mapRanges = MapDetector.DetectMaps(wad)
            .Select(m => (m.StartIndex, m.EndIndex))
            .ToList();

        var neededPatches = usage is null
            ? null
            : new HashSet<string>(textures.PatchNames, StringComparer.OrdinalIgnoreCase);

        // Marker-group buffering. While a group (F_START..F_END, P_START..P_END, ...) is
        // open and at least one lump of it passes the filter, the markers are implemented
        // together with the graphics so editors/engines classify them as flats/patches.
        // Without filtering (usage null) the markers are copied as plain lumps as before.
        string? groupStart = null;
        var groupBuffer = new List<(string Name, byte[] Data)>();

        void FlushGroup()
        {
            if (groupStart is null)
                return;

            bool any = false;
            foreach (var (name, data) in groupBuffer)
                any |= !outputNames.Contains(name);

            if (any)
            {
                if (outputNames.Add(groupStart))
                    builder.AddLump(groupStart, Array.Empty<byte>());
                foreach (var (name, data) in groupBuffer)
                {
                    if (!outputNames.Add(name))
                        continue;
                    builder.AddLump(name, data);
                    if (usage is not null && usage.Flats.Contains(name))
                        _result.FlatsCopied++;
                    _result.LumpsCopied++;
                }
                string end = MarkerEnds[groupStart];
                if (outputNames.Add(end))
                    builder.AddLump(end, Array.Empty<byte>());
            }

            groupStart = null;
            groupBuffer.Clear();
        }

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

            if (skipMapInfo && (lump.Name == "MAPINFO" || lump.Name == "ZMAPINFO"))
            {
                mapInfoSeen = true;
                continue;
            }

            if (skipFromBase.Contains(lump.Name))
                continue;

            // Start/end marker handling when filtering.
            if (usage is not null)
            {
                if (MarkerEnds.TryGetValue(lump.Name, out _))
                {
                    FlushGroup();
                    groupStart = lump.Name;
                    continue;
                }
                if (groupStart is not null && lump.Name == MarkerEnds[groupStart])
                {
                    FlushGroup();
                    continue;
                }
            }

            // Filtered resource WAD: keep only graphics the maps reference.
            bool included = usage is null
                || AlwaysCopyResourceLumps.Contains(lump.Name)
                || usage.Flats.Contains(lump.Name)
                || (neededPatches is not null && neededPatches.Contains(lump.Name));

            if (!included)
                continue;

            if (groupStart is not null && usage is not null)
            {
                if (!outputNames.Contains(lump.Name))
                    groupBuffer.Add((lump.Name, lump.ReadAll()));
                continue;
            }

            if (SingleWinningLumps.Contains(lump.Name) && outputNames.Contains(lump.Name))
                continue;

            if (outputNames.Contains(lump.Name))
            {
                _result.DuplicatesSkipped++;
                continue;
            }

            builder.AddLump(lump.Name, lump.ReadAll());
            outputNames.Add(lump.Name);
            if (usage is not null && usage.Flats.Contains(lump.Name))
                _result.FlatsCopied++;
            _result.LumpsCopied++;
        }

        FlushGroup(); // the WAD may lack the closing marker
    }

    /// <summary>Builds a ZDoom MAPINFO text with a level name per assigned map.
    /// Uses the classic form `map MAP01 "Name"` (level name as second token) which is
    /// accepted by both the classic and the namespaced ("new") MAPINFO parsers.</summary>
    private static (string? Text, int Count) BuildMapInfo(
        IReadOnlyList<(DetectedMap Map, string FinalName, string? LevelName)> assignments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// MAPINFO generado automáticamente por Community Wad Compiler");

        int named = 0;
        foreach (var (_, finalName, levelName) in assignments)
        {
            string name = (levelName ?? "").Trim();
            if (name.Length == 0)
                continue;

            named++;
            sb.AppendLine($"map {finalName} \"{SanitizeMapInfoString(name)}\"");
        }

        return named == 0 ? (null, 0) : (sb.ToString(), named);
    }

    private static string SanitizeMapInfoString(string value)
        => value.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
}