using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CommunityWadCompiler.Core.Localization;
using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Music;
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
    private static readonly HashSet<string> PaletteLumpNames = new(StringComparer.Ordinal)
    {
        "PLAYPAL", "COLORMAP", "TINTTAB", "RGBMAP", "PLAYPAL2",
    };

    /// <summary>
    /// Sprite marker names that delimit sprite, status-bar and font graphics in a WAD.
    /// </summary>
    private static readonly HashSet<string> SpriteMarkerNames = new(StringComparer.Ordinal)
    {
        "S_START", "S_END", "SS_START", "SS_END",
        "ST_START", "ST_END", "STRT_START", "STRT_END", "FM_START", "FM_END",
    };

    /// <summary>
    /// Start markers of the sprite/status-bar/font groups. Used both to collect the
    /// IWAD sprite names and to include whole groups from resources when
    /// <see cref="MergeOptions.IncludeSpriteLumps"/> is set.
    /// </summary>
    private static readonly HashSet<string> SpriteStartNames = new(StringComparer.Ordinal)
    {
        "S_START", "SS_START", "ST_START", "STRT_START", "FM_START",
    };

    /// <summary>
    /// Collects the "official" sprite-like names from the base WAD (IWAD):
    /// - lumps inside marked sprite/status-bar/font groups (S_START..S_END,
    ///   SS_START..SS_END, ST_START..ST_END, STRT/ FM groups), and
    /// - lumps NOT inside any marked group in the IWAD (status bar, ammo icons,
    ///   menu graphics, doomguy faces, ...), which in real IWADs sit unmarked
    ///   before the S_START marker (DOOM2.WAD: STBAR, STGNUM*, STF*, M_*, ...).
    /// These are the names the engine expects; we only copy matching lumps from
    /// resources (including unmarked overrides such as a sprite pack's STBAR).
    /// </summary>
    private static HashSet<string> CollectBaseSpriteNames(WadFile? baseWad)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (baseWad is null)
            return names;

        var mapRanges = MapDetector.DetectMaps(baseWad)
            .Select(m => (m.StartIndex, m.EndIndex))
            .ToList();

        string? groupStart = null;
        string? groupEnd = null;
        for (int i = 0; i < baseWad.Lumps.Count; i++)
        {
            var lump = baseWad.Lumps[i];
            if (mapRanges.Any(r => i >= r.Item1 && i < r.Item2))
                continue;

            // Generic marker-group tracking: any X_START opens a group that ends at its
            // paired X_END (or the explicit pairing when it differs, e.g. FF_START -> F_END).
            bool isStart = lump.Name.EndsWith("_START", StringComparison.Ordinal);
            bool isClose = groupStart is not null && lump.Name == groupEnd;
            if (isStart || isClose)
            {
                if (isStart)
                {
                    if (groupStart is null)
                    {
                        groupStart = lump.Name;
                        groupEnd = MarkerEnds.TryGetValue(lump.Name, out string? explicitEnd)
                            ? explicitEnd
                            : lump.Name[..^"_START".Length] + "_END";
                    }
                }
                else
                {
                    groupStart = null;
                    groupEnd = null;
                }
                continue;
            }

            // Lumps inside a marked group: only the sprite-family groups are "official"
            // sprite names; flat/patch/texture groups are not.
            if (groupStart is not null)
            {
                if (SpriteStartNames.Contains(groupStart))
                    names.Add(lump.Name);
                continue;
            }

            // Unmarked top-level lumps of the IWAD (status bar, menu, faces, ...): treat
            // them as sprite-like graphics so a resource pack can override STBAR, STF*, M_...
            if (TextureLumpNames.Contains(lump.Name)
                || PaletteLumpNames.Contains(lump.Name)
                || MarkerEnds.ContainsKey(lump.Name)
                || IsMusicLump(lump))
                continue;

            names.Add(lump.Name);
        }
        return names;
    }

    /// <summary>
    /// Non-texture lumps always implemented from a resource WAD even when filtering to
    /// used resources, because they are shared graphics state (animation tables).
    /// </summary>
    private static readonly HashSet<string> AlwaysCopyResourceLumps = new(StringComparer.Ordinal)
    {
        "ANIMATED",
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
        ["SS_START"] = "SS_END",
        ["ST_START"] = "ST_END",
        ["STRT_START"] = "STRT_END",
        ["FM_START"] = "FM_END",
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
            Report(CoreMessages.Get("Merge.OpenBaseWad"));
            WadFile? baseWad = LoadOptional(request.BaseWadPath, Report);

            Report(CoreMessages.Get("Merge.OpenInputs"));
            var inputs = new List<WadFile>(request.InputWadPaths.Count);
            foreach (string path in request.InputWadPaths)
                inputs.Add(WadFile.Open(path));

            Report(CoreMessages.Get("Merge.OpenResources"));
            var resources = new List<WadFile>(request.ResourceWadPaths.Count);
            foreach (string path in request.ResourceWadPaths)
                resources.Add(WadFile.Open(path));
            if (resources.Count == 0)
                Report(CoreMessages.Get("Merge.NoResourceWad"));

            Report(CoreMessages.Get("Merge.ScanMusic"));
            var musicByName = MusicLumpDetector.CollectAcrossWads(inputs.Concat<WadFile>(resources))
                .ToDictionary(c => c.FinalName, c => (c.WadPath, c.OriginalName), StringComparer.Ordinal);
            if (musicByName.Count > 0)
                Report(CoreMessages.Get("Merge.MusicFound", musicByName.Count));

            Report(CoreMessages.Get("Merge.DetectMaps"));
            var assignments = BuildAssignments(inputs, request, Warn);

            // Which textures/flats do the merged maps need? Needed only when resource
            // WADs are in play and the user asked to filter to used resources.
            UsedTextures? usage = null;
            if (resources.Count > 0 && request.Options.FilterToUsedResources)
            {
                Report(CoreMessages.Get("Merge.AnalyzeUsage"));
                usage = AnalyzeUsage(assignments);
                UnionAnimationLumpUsage(inputs, resources, usage);
                Report(CoreMessages.Get("Merge.UsageFound", usage.Walls.Count, usage.Flats.Count));
            }

            Report(CoreMessages.Get("Merge.MergeTextures"));
            IReadOnlyList<WadFile> textureSources = resources.Count > 0 ? resources : inputs;
            var textures = MergeTextures(baseWad, textureSources, request.Options);
            if (usage is not null)
            {
                int excluded = textures.ApplyUsageFilter(usage.Walls);
                _result.TexturesExcluded = excluded;
                Report(CoreMessages.Get("Merge.ExcludedTextures", excluded));
            }

            Report(CoreMessages.Get("Merge.Assemble"));
            string outputPath = BuildOutput(
                request, inputs, resources, assignments, textures, usage, musicByName, baseWad, Report, Warn);

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
            report(CoreMessages.Get("Merge.NoBaseWad"));
            return null;
        }
        return WadFile.Open(path);
    }

    // ------------------------------------------------------------------
    // Map assignments
    // ------------------------------------------------------------------

    private sealed record AssignmentInfo(
        DetectedMap Map,
        string FinalName,
        string? LevelName,
        string? MusicName,
        string? SkyName,
        string? Author,
        string? Status,
        string? LastModified,
        string? Sky2Name,
        double SkyScroll,
        double Sky2Scroll);

    private static IReadOnlyList<AssignmentInfo> BuildAssignments(
        List<WadFile> inputs,
        MergeRequest request,
        Action<string> warn)
    {
        var explicitMap = request.MapAssignments
            .ToDictionary(a => (a.WadPath, a.OriginalMapName), DefaultComparer.Instance);

        var result = new List<AssignmentInfo>();
        var seenFinals = new HashSet<string>(StringComparer.Ordinal);
        int autoCounter = 1;

        for (int w = 0; w < inputs.Count; w++)
        {
            var wad = inputs[w];
            foreach (var map in MapDetector.DetectMaps(wad))
            {
                string? final = null;
                AssignmentInfo? info = null;
                string? wadPath = wad.SourcePath;
                if (wadPath is not null && explicitMap.TryGetValue((wadPath, map.OriginalName), out MapAssignment? assignment))
                {
                    final = assignment.FinalMapName;
                    info = new AssignmentInfo(
                        map,
                        assignment.FinalMapName,
                        assignment.LevelName,
                        assignment.MusicName,
                        assignment.SkyName,
                        assignment.Author,
                        assignment.Status,
                        assignment.LastModified,
                        assignment.Sky2Name,
                        assignment.SkyScroll,
                        assignment.Sky2Scroll);
                }
                else if (request.Options.AutoAssignMaps)
                    final = $"MAP{autoCounter++:D2}";

                if (final is null)
                {
                    warn(CoreMessages.Get("Merge.MapNoSlot", map.OriginalName, wadPath ?? ""));
                    continue;
                }

                if (!seenFinals.Add(final))
                {
                    throw new InvalidOperationException(CoreMessages.Get("Merge.DuplicateSlot", final));
                }

                result.Add(info ?? new AssignmentInfo(map, final, null, null, "sky1", null, null, null, null, 0, 0));
            }
        }

        // Emit maps in slot order (MAP01, MAP02, ... MAP10, E1M1, ...), numerically aware so
        // MAP10 sorts after MAP09 instead of lexicographically before it.
        result.Sort(CompareByFinalName);
        return result;
    }

    private static int CompareByFinalName(AssignmentInfo a, AssignmentInfo b)
    {
        (string prefixA, int numA) = SplitSlotName(a.FinalName);
        (string prefixB, int numB) = SplitSlotName(b.FinalName);
        int cmp = string.Compare(prefixA, prefixB, StringComparison.OrdinalIgnoreCase);
        return cmp != 0 ? cmp : numA.CompareTo(numB);
    }

    /// <summary>Splits a slot name into its non-digit prefix and trailing numeric part,
    /// e.g. "MAP09" → ("MAP", 9), "E1M1" → ("E1M", 1), "BOSSR" → ("BOSSR", -1).</summary>
    private static (string Prefix, int Number) SplitSlotName(string slot)
    {
        if (string.IsNullOrEmpty(slot))
            return (slot ?? "", -1);

        int i = slot.Length;
        while (i > 0 && char.IsDigit(slot[i - 1]))
            i--;

        string number = slot[i..];
        return (slot[..i], number.Length == 0 ? -1 : int.Parse(number));
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
    private static UsedTextures AnalyzeUsage(IReadOnlyList<AssignmentInfo> assignments)
    {
        var used = new UsedTextures();
        foreach (var a in assignments)
            used.UnionWith(MapTextureAnalyzer.Analyze(a.Map));
        return used;
    }

    /// <summary>
    /// Expands the used set with the texture/flat names referenced by the ANIMDEFS/SWITCHES
    /// lumps of the WADs (first source wins per lump), so the resource filter keeps animated
    /// walls, switches, doors and animated flats the engine still processes at load time.
    /// </summary>
    private static void UnionAnimationLumpUsage(
        IEnumerable<WadFile> inputs,
        IEnumerable<WadFile> resources,
        UsedTextures usage)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var wad in inputs.Concat<WadFile>(resources))
        {
            foreach (string lumpName in new[] { "ANIMDEFS", "SWITCHES" })
            {
                if (wad.FindFirst(lumpName) is not Lump lump)
                    continue;
                string text = System.Text.Encoding.ASCII.GetString(lump.ReadAll());
                foreach (Match match in Regex.Matches(text, "[A-Za-z][A-Za-z0-9_]{0,7}"))
                    names.Add(match.Value.ToUpperInvariant());
            }
        }

        if (names.Count == 0)
            return;

        usage.Walls.UnionWith(names);
        usage.Flats.UnionWith(names);
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
        IReadOnlyList<AssignmentInfo> assignments,
        TextureMerger textures,
        UsedTextures? usage,
        Dictionary<string, (string WadPath, string OriginalName)> musicByName,
        WadFile? baseWad,
        Action<string> report,
        Action<string> warn)
    {
        var builder = new WadBuilder();
        var outputNames = new HashSet<string>(StringComparer.Ordinal);

        // 0. Generated MAPINFO (level names) comes first.
        (string? mapInfoText, int mapInfoCount) = request.Options.GenerateMapInfo
            ? BuildMapInfo(request, assignments)
            : (null, 0);
        bool mapInfoSeen = false;
        if (mapInfoText is not null)
        {
            builder.AddLump("MAPINFO", Encoding.UTF8.GetBytes(mapInfoText));
            outputNames.Add("MAPINFO");
            _result.Info.Add(CoreMessages.Get("Merge.MapInfoGenerated", mapInfoCount));
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

        // Collect sky texture names needed by assignments (from MAPINFO sky1/sky2)
        var neededSkyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            string sky = (a.SkyName ?? "sky1").Trim();
            if (sky.Length > 0)
                neededSkyNames.Add(sky);
            string sky2 = (a.Sky2Name ?? "").Trim();
            if (sky2.Length > 0)
                neededSkyNames.Add(sky2);
        }

        // 2. Non-map lumps from the inputs, deduplicated (first wins).
        var skipFromBase = BuildBaseSkipSet(baseWad, request.Options);
        var baseSpriteNames = CollectBaseSpriteNames(baseWad);
        bool zdoomTexturesSeen = false;

        foreach (var wad in inputs)
            CopyGenericLumps(wad, builder, outputNames, skipFromBase, null, textures,
                ref zdoomTexturesSeen, mapInfoText is not null, ref mapInfoSeen, warn,
                request.Options.IncludePaletteLumps, request.Options.IncludeSpriteLumps,
                baseSpriteNames, copyMusic: false, neededSkyNames, isResourceWad: false);

        foreach (var wad in resources)
            CopyGenericLumps(wad, builder, outputNames, skipFromBase, usage, textures,
                ref zdoomTexturesSeen, mapInfoText is not null, ref mapInfoSeen, warn,
                request.Options.IncludePaletteLumps, request.Options.IncludeSpriteLumps,
                baseSpriteNames, copyMusic: false, neededSkyNames, isResourceWad: true);

        if (mapInfoText is not null && mapInfoSeen)
            warn(CoreMessages.Get("Merge.MapInfoSkipped"));

        if (zdoomTexturesSeen)
            warn(CoreMessages.Get("Merge.ZdoomTexturesDropped"));

        // 3. Maps in slot order.
        foreach (var a in assignments)
        {
            report(CoreMessages.Get("Merge.CopyingMap", a.Map.OriginalName, a.FinalName));
            var lumps = a.Map.Wad.Lumps;
            for (int i = a.Map.StartIndex; i < a.Map.EndIndex; i++)
            {
                string name = i == a.Map.StartIndex ? a.FinalName : lumps[i].Name;
                builder.AddLump(name, lumps[i].ReadAll());
            }
            _result.MapsAdded++;
        }

        // 3.5. Music lumps selected for each map (deduplicated; first existing wins).
        var wadsByPath = new Dictionary<string, WadFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var wad in inputs.Concat(resources))
            if (wad.SourcePath is not null)
                wadsByPath[wad.SourcePath] = wad;

        var warnedMusic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in assignments)
        {
            string m = (a.MusicName ?? "").Trim();
            if (m.Length == 0 || outputNames.Contains(m))
                continue;

            if (musicByName.TryGetValue(m, out var src)
                && wadsByPath.TryGetValue(src.WadPath, out WadFile? srcWad)
                && srcWad.FindFirst(src.OriginalName) is Lump musicLump)
            {
                builder.AddLump(m, musicLump.ReadAll());
                outputNames.Add(m);
                _result.MusicCopied++;
                if (!string.Equals(m, src.OriginalName, StringComparison.Ordinal))
                {
                    _result.Info.Add(CoreMessages.Get("Merge.MusicRenamed", src.OriginalName, Path.GetFileName(src.WadPath), m));
                }
            }
            else if (warnedMusic.Add(m))
            {
                warn(CoreMessages.Get("Merge.MusicNotInWads", m));
            }
        }

        // 3.5a. Intermission music: copy the chosen lump if it is not already in the
        // output and is not provided by an external file (3.5b copies those).
        string intermissionLump = (request.IntermissionMusic ?? "").Trim();
        if (intermissionLump.Length > 0
            && !outputNames.Contains(intermissionLump)
            && (request.ExternalMusicFiles is null || !request.ExternalMusicFiles.ContainsKey(intermissionLump)))
        {
            if (musicByName.TryGetValue(intermissionLump, out var src)
                && wadsByPath.TryGetValue(src.WadPath, out WadFile? srcWad)
                && srcWad.FindFirst(src.OriginalName) is Lump musicLump)
            {
                builder.AddLump(intermissionLump, musicLump.ReadAll());
                outputNames.Add(intermissionLump);
                _result.MusicCopied++;
                _result.Info.Add(CoreMessages.Get("Merge.IntermissionMusicAdded", intermissionLump, Path.GetFileName(src.WadPath)));
            }
            else if (warnedMusic.Add(intermissionLump))
            {
                warn(CoreMessages.Get("Merge.IntermissionMusicMissing", intermissionLump));
            }
        }

        // 3.5b. External music files: copy files chosen by the user into the output WAD.
        if (request.ExternalMusicFiles is { Count: > 0 } externalMusic)
        {
            var validExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mid", ".mod", ".it", ".xm", ".s3m",
            };

            foreach (var (lumpName, filePath) in externalMusic)
            {
                if (outputNames.Contains(lumpName))
                    continue;

                if (!File.Exists(filePath))
                {
                    warn(CoreMessages.Get("Merge.ExternalMusicMissing", lumpName, filePath));
                    continue;
                }

                string ext = Path.GetExtension(filePath);
                if (!validExts.Contains(ext))
                {
                    warn(CoreMessages.Get("Merge.ExternalMusicUnsupported", lumpName, ext));
                    continue;
                }

                byte[] data = File.ReadAllBytes(filePath);
                builder.AddLump(lumpName, data);
                outputNames.Add(lumpName);
                _result.MusicCopied++;
                _result.Info.Add(CoreMessages.Get("Merge.ExternalMusicAdded", lumpName, Path.GetFileName(filePath)));
            }
        }

        // 3.6. Sky textures: copy needed sky lumps from resources to output.
        if (neededSkyNames.Count > 0)
        {
            var wadsByPath2 = new Dictionary<string, WadFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var wad in resources)
                if (wad.SourcePath is not null)
                    wadsByPath2[wad.SourcePath] = wad;

            var warnedSky = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var skyName in neededSkyNames)
            {
                if (outputNames.Contains(skyName))
                    continue;

                bool copied = false;
                foreach (var resWad in resources)
                {
                    var lump = resWad.FindFirst(skyName);
                    if (lump is not null)
                    {
                        builder.AddLump(skyName, lump.ReadAll());
                        outputNames.Add(skyName);
                        _result.LumpsCopied++;
                        copied = true;
                        break;
                    }
                }
                if (!copied && warnedSky.Add(skyName))
                {
                    warn(CoreMessages.Get("Merge.SkyMissing", skyName));
                }
            }
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
            if (!TextureLumpNames.Contains(lump.Name) && !PaletteLumpNames.Contains(lump.Name))
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
        Action<string> warn,
        bool includePaletteLumps,
        bool includeSpriteLumps,
        HashSet<string> baseSpriteNames,
        bool copyMusic,
        HashSet<string>? neededSkyNames,
        bool isResourceWad)
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

            // Skip music lumps when copyMusic is false (they'll be copied in section 3.5 if assigned).
            if (!copyMusic && IsMusicLump(lump))
                continue;

            // Start/end marker handling when filtering. This runs BEFORE the base-WAD skip
            // so that marker groups (S_START/S_END, F_START/F_END, ...) can open in resource
            // WADs even though the IWAD itself contains the same marker names.
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
            bool isSpriteGroup = includeSpriteLumps
                && groupStart is not null
                && SpriteStartNames.Contains(groupStart);

            // When IncludeSpriteLumps is ON, copy lumps that match IWAD sprite names
            // (regardless of whether they're between markers in the resource WAD).
            bool isMatchingBaseSprite = includeSpriteLumps
                && baseSpriteNames.Contains(lump.Name);

            // With a base IWAD we skip lumps the IWAD already provides (SkipBaseWadResources),
            // otherwise the PWAD would duplicate them. Sprite content (groups or IWAD-name
            // matches) is exempt when IncludeSpriteLumps is ON: the user explicitly asked for
            // the pack's sprite/status-bar/font replacements, including overrides of IWAD lumps.
            bool isSpriteContent = isSpriteGroup
                || isMatchingBaseSprite
                || (includeSpriteLumps && SpriteMarkerNames.Contains(lump.Name));

            if (skipFromBase.Contains(lump.Name) && !isSpriteContent)
                continue;

            // Skip sky textures that aren't needed (only copy needed sky from resources).
            if (neededSkyNames is not null && !neededSkyNames.Contains(lump.Name))
            {
                // But still allow the lump if it passes other filters (flats, patches, etc.)
                // Only skip if it's purely a sky texture not in our needed list.
                // We'll let the normal filter logic handle this; just don't force-include sky.
            }

bool included = usage is null
                || isSpriteGroup
                || isMatchingBaseSprite
                || (includePaletteLumps && PaletteLumpNames.Contains(lump.Name))
                || (includeSpriteLumps && SpriteMarkerNames.Contains(lump.Name))
                || AlwaysCopyResourceLumps.Contains(lump.Name)
                || usage.Flats.Contains(lump.Name)
                || (neededPatches is not null && neededPatches.Contains(lump.Name));

            // Sky texture filtering:
            // - For INPUT WADs (PWADs aportados): NEVER copy sky textures, regardless of neededSkyNames.
            //   They should only come from resource WADs.
            // - For RESOURCE WADs: only copy sky textures that are in neededSkyNames.
            //   Other sky textures are skipped unless needed for other reasons (flat, patch, etc.).
            if (neededSkyNames is not null)
            {
                bool isSkyTexture = IsSkyTextureName(lump.Name);
                if (isSkyTexture)
                {
                    if (!isResourceWad)
                    {
                        // Input WAD: never copy sky textures
                        included = false;
                    }
                    else
                    {
                        // Resource WAD: only copy if in neededSkyNames
                        if (!neededSkyNames.Contains(lump.Name))
                        {
                            // Check if needed for other reasons (flat, patch, palette, sprite)
                            bool neededForOtherReasons = false;
                            if (usage is not null)
                            {
                                neededForOtherReasons = usage.Flats.Contains(lump.Name)
                                    || (neededPatches is not null && neededPatches.Contains(lump.Name))
                                    || (includePaletteLumps && PaletteLumpNames.Contains(lump.Name))
                                    || (includeSpriteLumps && SpriteMarkerNames.Contains(lump.Name))
                                    || isSpriteGroup
                                    || isMatchingBaseSprite
                                    || AlwaysCopyResourceLumps.Contains(lump.Name);
                            }
                            if (!neededForOtherReasons)
                                included = false;
                        }
                    }
                }
            }

            if (!included)
                continue;

            // For INPUT WADs (PWADs aportados): NEVER copy any non-map lumps.
            // They should ONLY provide map data (THINGS, LINEDEFS, etc.).
            // All textures, flats, sky, palette, sprites, music, etc. come from resource WADs.
            if (!isResourceWad)
            {
                included = false;
            }

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
    /// Uses the classic form `map MAP01 "Name"` plus classic property lines WITHOUT
    /// `=`/quotes (`music D_RUNNIN`) — accepted by the classic and namespaced parsers
    /// of GZDoom/SLADE (verified against a known-good classic MAPINFO).</summary>
    private static (string? Text, int Count) BuildMapInfo(
        MergeRequest request,
        IReadOnlyList<AssignmentInfo> assignments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// MAPINFO generado automáticamente por Community Wad Compiler");

        string project = (request.ProjectName ?? "").Trim();
        string versionPrefix = (request.VersionPrefix ?? "").Trim();
        if (project.Length > 0)
            sb.AppendLine($"// Proyecto: {SanitizeMapInfoString(project)}");
        string fullVersion = versionPrefix.Length > 0
            ? $"{versionPrefix}.{DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)}"
            : $"{CompilerInfo.Version}.{DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)}";
        sb.AppendLine($"// Versión: {SanitizeMapInfoString(fullVersion)}");
        sb.AppendLine($"// Compilado: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

        string intermission = (request.IntermissionMusic ?? "").Trim();
        if (intermission.Length > 0)
        {
            sb.AppendLine("gameinfo");
            sb.AppendLine("{");
            sb.AppendLine($"    intermissionmusic = \"{SanitizeMapInfoString(intermission).Replace(" ", "")}\"");
            sb.AppendLine("}");
        }

        int count = 0;
        foreach (var a in assignments)
        {
            string name = (a.LevelName ?? "").Trim();
            string music = (a.MusicName ?? "").Trim();
            string sky = (a.SkyName ?? "").Trim();
            string sky2 = (a.Sky2Name ?? "").Trim();
            string authorComment = (a.Author ?? "").Trim();
            string status = (a.Status ?? "").Trim();
            string modified = (a.LastModified ?? "").Trim();
            if (name.Length == 0 && music.Length == 0
                && authorComment.Length == 0 && status.Length == 0 && modified.Length == 0
                && sky.Length == 0 && sky2.Length == 0)
                continue;

            count++;
            if (authorComment.Length > 0)
                sb.AppendLine($"// Autor: {SanitizeMapInfoString(authorComment)}");
            if (status.Length > 0)
                sb.AppendLine($"// Estado: {SanitizeMapInfoString(status)}");
            if (modified.Length > 0)
                sb.AppendLine($"// Última modificación: {SanitizeMapInfoString(modified)}");
            sb.AppendLine($"map {a.FinalName} \"{SanitizeMapInfoString(name)}\"");
            sb.AppendLine("{");
            if (music.Length > 0)
                sb.AppendLine($"    music = \"{SanitizeMapInfoString(music).Replace(" ", "")}\"");
            if (sky.Length > 0)
                sb.AppendLine($"    sky1 = \"{SanitizeMapInfoString(sky).Replace(" ", "")}\"{SkySpeedSuffix(a.SkyScroll)}");
            if (sky2.Length > 0)
                sb.AppendLine($"    sky2 = \"{SanitizeMapInfoString(sky2).Replace(" ", "")}\"{SkySpeedSuffix(a.Sky2Scroll)}");
            sb.AppendLine("}");
        }

        return count == 0 && intermission.Length == 0 ? (null, 0) : (sb.ToString(), count);
    }

    /// <summary>ZDoom sky syntax: `sky1 = "SKY1"` plus an optional rotation-speed offset
    /// (`sky1 = "SKY1", 0.5`). Speed 0 is the engine default and is omitted.</summary>
    private static string SkySpeedSuffix(double speed)
        => speed == 0
            ? ""
            : $", {speed.ToString("0.##", CultureInfo.InvariantCulture)}";

    private static string SanitizeMapInfoString(string value)
        => value.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");

    /// <summary>Detects if a lump contains MUS, MIDI, IT or MOD music data by checking its header.</summary>
    private static bool IsMusicLump(Lump lump) => MusicLumpDetector.IsMusicData(lump);

    /// <summary>Checks if a lump name looks like a sky texture by common naming patterns.
    /// Common sky texture names in Doom: SKY1, SKY2, SKY3, RSKY1, RSKY2, RSKY3, F_SKY1, F_SKY2, etc.</summary>
    private static bool IsSkyTextureName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        // Common sky texture prefixes/suffixes (case-insensitive)
        if (name.StartsWith("SKY", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("RSKY", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("F_SKY", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("SKY", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}