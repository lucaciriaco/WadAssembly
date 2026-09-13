using System.Text;
using WadAssembly.Core.Maps;
using WadAssembly.Core.Merge;
using WadAssembly.Core.Textures;
using WadAssembly.Core.WadFormat;
using Xunit;

namespace WadAssembly.Tests;

public class TextureUsageTests
{
    // ------------------------------------------------------------------
    // Analyzer: classic map
    // ------------------------------------------------------------------

    [Fact]
    public void ClassicMap_CollectsWallAndFlatNames()
    {
        string dir = TempDir();
        try
        {
            string mapWad = Path.Combine(dir, "map.wad");
            BuildClassicMapWad(mapWad, "MAP01");

            using var wad = WadFile.Open(mapWad);
            var map = MapDetector.DetectMaps(wad)[0];
            var used = MapTextureAnalyzer.Analyze(map);

            Assert.Contains("TEX1", used.Walls);
            Assert.Contains("TEXMID", used.Walls);
            Assert.DoesNotContain("TEX2", used.Walls);
            Assert.DoesNotContain("-", used.Walls);
            Assert.Contains("FLAT1", used.Flats);
            Assert.Contains("F_SKY1", used.Flats);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UdmfMap_CollectsNamesFromTextmap()
    {
        string dir = TempDir();
        try
        {
            string mapWad = Path.Combine(dir, "map_udmf.wad");
            const string textmap =
                "// comentario\n" +
                "namespace = \"zdoom\";\n" +
                "sidedef\n{\n" +
                "texturetop = \"TEXWALL\";\n" +
                "texturebottom = \"-\";\n" +
                "texturemiddle = 'TEXMID';\n" +
                "offsetx = 0;\n}\n" +
                "sector\n{\n" +
                "floorpic = \"FLATF\";\n" +
                "ceilingpic = \"F_SKY1\";\n" +
                "lightlevel = 128;\n}\n";

            var builder = new WadBuilder();
            builder.AddLump("MAP01", new byte[1]);
            builder.AddLump("TEXTMAP", System.Text.Encoding.ASCII.GetBytes(textmap));
            builder.AddLump("ENDMAP", new byte[1]);
            builder.Write(mapWad);

            using var wad = WadFile.Open(mapWad);
            var map = MapDetector.DetectMaps(wad)[0];
            var used = MapTextureAnalyzer.Analyze(map);

            Assert.True(map.IsUdmf);
            Assert.Contains("TEXWALL", used.Walls);
            Assert.Contains("TEXMID", used.Walls);
            Assert.DoesNotContain("-", used.Walls);
            Assert.Contains("FLATF", used.Flats);
            Assert.Contains("F_SKY1", used.Flats);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExtendedSectorLayout_CollectsFlatNamesFromOffset4()
    {
        string dir = TempDir();
        try
        {
            string mapWad = Path.Combine(dir, "map_ext.wad");
            var builder = new WadBuilder();
            builder.AddLump("MAP01", new byte[1]);
            builder.AddLump("THINGS", new byte[10]);
            builder.AddLump("LINEDEFS", new byte[14]);
            builder.AddLump("SIDEDEFS", MakeSidedefs(("TEX1", "", "TEXMID")));
            builder.AddLump("VERTEXES", new byte[4]);
            builder.AddLump("SEGS", new byte[12]);
            builder.AddLump("SSECTORS", new byte[4]);
            builder.AddLump("NODES", new byte[28]);
            builder.AddLump("SECTORS", MakeExtendedSectors(("FLAT1", "F_SKY1"), ("FLAT2", "CEIL1_1")));
            builder.AddLump("REJECT", new byte[0]);
            builder.AddLump("BLOCKMAP", new byte[2]);
            builder.Write(mapWad);

            using var wad = WadFile.Open(mapWad);
            var map = MapDetector.DetectMaps(wad)[0];
            var used = MapTextureAnalyzer.Analyze(map);

            Assert.Contains("FLAT1", used.Flats);
            Assert.Contains("F_SKY1", used.Flats);
            Assert.Contains("FLAT2", used.Flats);
            Assert.Contains("CEIL1_1", used.Flats);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Pipeline: resource WAD filtered to used textures/flats
    // ------------------------------------------------------------------

    [Fact]
    public void ResourceWad_FiltersToUsedTexturesAndFlats()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildResourceWad(resourceWad);
            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = true },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // Texture TEX1 is used; TEX2 is defined in the resource WAD but unused.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Single(textures.Textures);
            Assert.Equal("TEX1", textures.Textures[0].Name);

            // Only the patch used by TEX1 survives in PNAMES.
            var pnames = PnamesList.Read(wad.FindLast("PNAMES")!.ReadAll());
            Assert.Equal(new[] { "PATCHA" }, pnames.Names);

            // Flat FLAT1 is used; FLAT2 is not. PLAYPAL is always implemented.
            Assert.NotNull(wad.FindFirst("FLAT1"));
            Assert.Null(wad.FindFirst("FLAT2"));
            Assert.NotNull(wad.FindFirst("PLAYPAL"));
            Assert.NotNull(wad.FindFirst("PATCHA"));
            Assert.Null(wad.FindFirst("PATCHB"));

            // Marker groups survive so SLADE/Doom recognize flats and patches.
            Assert.NotNull(wad.FindFirst("F_START"));
            Assert.NotNull(wad.FindFirst("F_END"));
            Assert.NotNull(wad.FindFirst("P_START"));
            Assert.NotNull(wad.FindFirst("P_END"));
            int fStart = wad.Lumps.ToList().FindIndex(l => l.Name == "F_START");
            int fFlat = wad.Lumps.ToList().FindIndex(l => l.Name == "FLAT1");
            int fEnd = wad.Lumps.ToList().FindIndex(l => l.Name == "F_END");
            Assert.True(fStart >= 0 && fStart < fFlat && fFlat < fEnd);

            // The map was implemented.
            Assert.NotNull(wad.FindFirst("MAP05"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WithoutResourceWads_BehavesAsBefore()
    {
        string dir = TempDir();
        try
        {
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = true },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            using var wad = WadFile.Open(outputWad);
            Assert.NotNull(wad.FindFirst("MAP05"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResourceWad_AnimdefsSwitchTextures_AreKeptDespiteFilter()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimdefsResourceWad(resourceWad);
            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = true },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // ANIMDEFS references SW1BRCO/SW2BRCO (animated switch); neither is used by
            // the map's geometry, yet both must survive so the engine can load ANIMDEFS.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(new[] { "SW1BRCO", "SW2BRCO", "TEX1" }, textures.Textures.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));

            var pnames = PnamesList.Read(wad.FindLast("PNAMES")!.ReadAll());
            Assert.Contains("PATCHA", pnames.Names);
            Assert.Contains("PATCHB", pnames.Names);
            Assert.Contains("PATCHC", pnames.Names);
            Assert.NotNull(wad.FindFirst("PATCHA"));
            Assert.NotNull(wad.FindFirst("PATCHB"));
            Assert.NotNull(wad.FindFirst("PATCHC"));

            // Unused flat FLAT2 keeps being excluded.
            Assert.Null(wad.FindFirst("FLAT2"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Pipeline: IncludeSpriteLumps keeps sprites, status-bar and fonts
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeSpriteLumps_CopiesStatusBarAndSpritesFromResourceWad()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildSpriteResourceWad(resourceWad);
            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    IncludeSpriteLumps = true,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // Status-bar graphics (ST group) are copied whole with their markers.
            Assert.NotNull(wad.FindFirst("STBAR"));
            Assert.NotNull(wad.FindFirst("STGNUM0"));
            Assert.NotNull(wad.FindFirst("ST_START"));
            Assert.NotNull(wad.FindFirst("ST_END"));
            int stStart = wad.Lumps.ToList().FindIndex(l => l.Name == "ST_START");
            int stBar = wad.Lumps.ToList().FindIndex(l => l.Name == "STBAR");
            int stEnd = wad.Lumps.ToList().FindIndex(l => l.Name == "ST_END");
            Assert.True(stStart >= 0 && stStart < stBar && stBar < stEnd);

            // Regular sprites (S group) are copied whole with their markers.
            Assert.NotNull(wad.FindFirst("TROOA1"));
            Assert.NotNull(wad.FindFirst("S_START"));
            Assert.NotNull(wad.FindFirst("S_END"));

            // Fonts (FM group) are copied whole with their markers.
            Assert.NotNull(wad.FindFirst("FONTA"));
            Assert.NotNull(wad.FindFirst("FM_START"));
            Assert.NotNull(wad.FindFirst("FM_END"));

            // The map is still implemented.
            Assert.NotNull(wad.FindFirst("MAP05"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WithoutIncludeSpriteLumps_StatusBarIsStillFiltered()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildSpriteResourceWad(resourceWad);
            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    IncludeSpriteLumps = false,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            Assert.Null(wad.FindFirst("STBAR"));
            Assert.Null(wad.FindFirst("TROOA1"));
            Assert.Null(wad.FindFirst("FONTA"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NoResourcesImported_WhenEveryIncludeOptionOff_OnlyMapsAndMapInfo()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildSpriteResourceWad(resourceWad);
            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05", LevelName: "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = false,
                    IncludePaletteLumps = false,
                    IncludeSpriteLumps = false,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // Sprites, status bar and fonts are excluded.
            Assert.Null(wad.FindFirst("STBAR"));
            Assert.Null(wad.FindFirst("STGNUM0"));
            Assert.Null(wad.FindFirst("TROOA1"));
            Assert.Null(wad.FindFirst("FONTA"));
            Assert.Null(wad.FindFirst("S_START"));
            Assert.Null(wad.FindFirst("ST_START"));
            Assert.Null(wad.FindFirst("FM_START"));

            // Palette lumps are not implemented.
            Assert.Null(wad.FindFirst("PLAYPAL"));

            // No textures, flats or patches either: "todo desactivado" means the output
            // only carries the maps and the generated MAPINFO.
            Assert.Null(wad.FindFirst("PNAMES"));
            Assert.Null(wad.FindFirst("TEXTURE1"));
            Assert.Null(wad.FindFirst("FLAT1"));
            Assert.Null(wad.FindFirst("PATCHA"));

            // The map and the generated MAPINFO are still there.
            Assert.NotNull(wad.FindFirst("MAP05"));
            var mapInfo = wad.FindFirst("MAPINFO");
            Assert.NotNull(mapInfo);
            Assert.Contains("MAP05", Encoding.UTF8.GetString(mapInfo!.ReadAll()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IncludeSpriteLumps_MatchesIwadStatusBarNamesOutsideMarkers()
    {
        string dir = TempDir();
        try
        {
            string baseWad = Path.Combine(dir, "base.wad");
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            // IWAD resembling DOOM2.WAD: the status bar sits UNMARKED before S_START, and the
            // merge must harvest STBAR/STGNUM0 as sprite-like base names from there.
            var baseBuilder = new WadBuilder();
            baseBuilder.AddLump("STBAR", new byte[8]);
            baseBuilder.AddLump("STGNUM0", new byte[8]);
            baseBuilder.AddLump("S_START", new byte[0]);
            baseBuilder.AddLump("TROOA1", new byte[8]);
            baseBuilder.AddLump("S_END", new byte[0]);
            baseBuilder.Write(baseWad);

            // Resource WAD where the status-bar lumps are NOT inside markers.
            var resBuilder = new WadBuilder();
            resBuilder.AddLump("PNAMES", MakePnames("PATCHA"));
            resBuilder.AddLump("TEXTURE1", MakeTexture1(MakeTexture("TEX1", 64, 64, (0, 0, 0))));
            resBuilder.AddLump("P_START", new byte[0]);
            resBuilder.AddLump("PATCHA", new byte[8]);
            resBuilder.AddLump("P_END", new byte[0]);
            resBuilder.AddLump("F_START", new byte[0]);
            resBuilder.AddLump("FLAT1", new byte[4096]);
            resBuilder.AddLump("F_END", new byte[0]);
            resBuilder.AddLump("STBAR", new byte[8]);
            resBuilder.AddLump("STGNUM0", new byte[8]);
            resBuilder.AddLump("PLAYPAL", new byte[768]);
            resBuilder.Write(resourceWad);

            BuildClassicMapWad(mapWad, "MAP05");

            var request = new MergeRequest
            {
                BaseWadPath = baseWad,
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    IncludeSpriteLumps = true,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);
            Assert.NotNull(wad.FindFirst("STBAR"));
            Assert.NotNull(wad.FindFirst("STGNUM0"));
            Assert.NotNull(wad.FindFirst("MAP05"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Numbered (unmanifested) animated texture runs
    // ------------------------------------------------------------------

    [Fact]
    public void ExpandAnimatedTextureRuns_KeepsWholeNumberedRunUsedByName()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    ExpandAnimatedTextureRuns = true,
                    MaxAnimatedTextureFrames = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // BIGDOOR3 is used and its run BIGDOOR1..7 fits the cap: the whole run is kept.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(
                new[] { "BIGDOOR1", "BIGDOOR2", "BIGDOOR3", "BIGDOOR4", "BIGDOOR5", "BIGDOOR6", "BIGDOOR7" },
                textures.Textures.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));

            // Numbered variant cache (SHAWN1..12) is untouched: no frame is used by the map.
            Assert.DoesNotContain(textures.Textures, t => t.Name.StartsWith("SHAWN", StringComparison.Ordinal));

            // No ANIMDEFS is produced unless the generation option is on.
            Assert.Null(wad.FindFirst("ANIMDEFS"));
            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Null(wad.FindFirst("SWITCHES"));

            // Flats use their own numbered run: WATR2 is used, so WATR1..4 arrive whole.
            Assert.NotNull(wad.FindFirst("WATR1"));
            Assert.NotNull(wad.FindFirst("WATR2"));
Assert.NotNull(wad.FindFirst("WATR3"));
            Assert.Null(wad.FindFirst("ANIMDEFS"));
            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Null(wad.FindFirst("SWITCHES"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExpandAnimatedTextureRuns_LongVariantRunNotExpanded()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "SHAWN4", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    ExpandAnimatedTextureRuns = true,
                    MaxAnimatedTextureFrames = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // The SHAWN run is 12 frames long (a variant cache, not an animation): only the
            // used frame survives; the big flat run still qualifies.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(new[] { "SHAWN4" }, textures.Textures.Select(t => t.Name));
            Assert.NotNull(wad.FindFirst("WATR4"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExpandAnimatedTextureRuns_DisabledByDefault()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // Without the option nothing extra is imported: only the used frame and flat.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(new[] { "BIGDOOR3" }, textures.Textures.Select(t => t.Name));
            Assert.NotNull(wad.FindFirst("WATR2"));
            Assert.Null(wad.FindFirst("WATR1"));
            Assert.Null(wad.FindFirst("WATR3"));
            Assert.Null(wad.FindFirst("ANIMDEFS"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimdefsForAnimatedRuns_CreatesAnimdefsForExpandedRuns()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    // Generating ANIMDEFS implies importing the whole runs even though
                    // ExpandAnimatedTextureRuns is left off.
                    GenerateAnimdefsForAnimatedRuns = true,
                    AnimatedPrefixes = "BIGDOOR,WATR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // The whole used runs are imported (the ANIMDEFS would reference them).
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(
                new[] { "BIGDOOR1", "BIGDOOR2", "BIGDOOR3", "BIGDOOR4", "BIGDOOR5", "BIGDOOR6", "BIGDOOR7" },
                textures.Textures.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.NotNull(wad.FindFirst("WATR4"));

            // One ANIMDEFS lump declaring walls as animatedTexture and flats as animatedFloor.
            Assert.Equal(1, wad.Lumps.Count(l => l.Name == "ANIMDEFS"));
            string animdefs = NormalizeWs(System.Text.Encoding.UTF8.GetString(wad.FindFirst("ANIMDEFS")!.ReadAll()));
            Assert.Contains("animatedTexture 8, { BIGDOOR1, BIGDOOR2, BIGDOOR3, BIGDOOR4, BIGDOOR5, BIGDOOR6, BIGDOOR7 }", animdefs);
            Assert.Contains("animatedFloor 8, { WATR1, WATR2, WATR3, WATR4 }", animdefs);

            // The long variant cache stays out of both the output and the ANIMDEFS lump.
            Assert.DoesNotContain("SHAWN", animdefs);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimdefsForAnimatedRuns_AppendsToExistingAnimdefsLump()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimdefsPlusRunsResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimdefsForAnimatedRuns = true,
                    AnimatedPrefixes = "BIGDOOR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // Single merged lump: the WAD's own ANIMDEFS text is kept, the generated entries
            // are appended to it rather than replacing it.
            Assert.Equal(1, wad.Lumps.Count(l => l.Name == "ANIMDEFS"));
            string animdefs = NormalizeWs(System.Text.Encoding.UTF8.GetString(wad.FindFirst("ANIMDEFS")!.ReadAll()));
            Assert.Contains("animatedTexture TEX99A, TEX99B, 8", animdefs);
            Assert.Contains("animatedTexture 8, { BIGDOOR1, BIGDOOR2, BIGDOOR3, BIGDOOR4, BIGDOOR5, BIGDOOR6, BIGDOOR7 }", animdefs);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Binary ANIMATED / SWITCHES lumps (Boom format, Odamex/vanilla renderer)
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateAnimatedLumpForRuns_WritesBinaryAnimatedLump()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    // Generating the ANIMATED lump implies importing the whole runs even
                    // though ExpandAnimatedTextureRuns is left off.
                    GenerateAnimatedLumpForRuns = true,
                    AnimatedPrefixes = "BIGDOOR,WATR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // The whole used runs are imported as before.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(
                new[] { "BIGDOOR1", "BIGDOOR2", "BIGDOOR3", "BIGDOOR4", "BIGDOOR5", "BIGDOOR6", "BIGDOOR7" },
                textures.Textures.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.NotNull(wad.FindFirst("WATR4"));

            // One binary ANIMATED lump: 2 entries (wall + flat) of 23 bytes + terminator 0xFF.
            Assert.Equal(1, wad.Lumps.Count(l => l.Name == "ANIMATED"));
            Assert.Null(wad.FindFirst("ANIMDEFS"));
            byte[] animated = wad.FindFirst("ANIMATED")!.ReadAll();
            Assert.Equal(47, animated.Length);
            Assert.Equal(0xFF, animated[46]);

            (byte IsTexture, string EndName, string StartName, int Speed) wall =
                ReadAnimatedEntry(animated, 0);
            Assert.Equal(1, wall.IsTexture);
            Assert.Equal("BIGDOOR7", wall.EndName);
            Assert.Equal("BIGDOOR1", wall.StartName);
            Assert.Equal(8, wall.Speed);

            (byte IsTexture, string EndName, string StartName, int Speed) flat =
                ReadAnimatedEntry(animated, 1);
            Assert.Equal(0, flat.IsTexture);
            Assert.Equal("WATR4", flat.EndName);
            Assert.Equal("WATR1", flat.StartName);
            Assert.Equal(8, flat.Speed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimatedAndSwitches_MergeWithExistingBinaryLumps()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildBinaryLumpsResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "FLAT1", "SW1BRN");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimatedLumpForRuns = true,
                    GenerateSwitchesLumpForPairs = true,
                    AnimatedPrefixes = "BIGDOOR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // The WAD's existing 1-entry ANIMATED lump is kept and the generated wall run
            // entry is appended: 2 entries + terminator (no numbered flats in this WAD).
            Assert.Equal(1, wad.Lumps.Count(l => l.Name == "ANIMATED"));
            byte[] animated = wad.FindFirst("ANIMATED")!.ReadAll();
            Assert.Equal(47, animated.Length);
            Assert.Equal(0xFF, animated[46]);
            var existing = ReadAnimatedEntry(animated, 0);
            Assert.Equal(1, existing.IsTexture);
            Assert.Equal("BIGDOOR9", existing.EndName);
            Assert.Equal("BIGDOOR8", existing.StartName);
            Assert.Equal(4, existing.Speed);
            var added = ReadAnimatedEntry(animated, 1);
            Assert.Equal("BIGDOOR7", added.EndName);
            Assert.Equal("BIGDOOR1", added.StartName);
            Assert.Equal(8, added.Speed);

            // SW2BRN survives the filter (it is the partner of the used SW1BRN).
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Contains(textures.Textures, t => t.Name == "SW1BRN");
            Assert.Contains(textures.Textures, t => t.Name == "SW2BRN");

            // The WAD's existing 1-entry SWITCHES lump is kept and the generated pair appended.
            Assert.Equal(1, wad.Lumps.Count(l => l.Name == "SWITCHES"));
            byte[] switchesLump = wad.FindFirst("SWITCHES")!.ReadAll();
            Assert.Equal(40, switchesLump.Length);
            var pad = ReadSwitchEntry(switchesLump, 0);
            Assert.Equal("SW1PAD", pad.Off);
            Assert.Equal("SW2PAD", pad.On);
            Assert.Equal(0, pad.Flag);
            var pair = ReadSwitchEntry(switchesLump, 1);
            Assert.Equal("SW1BRN", pair.Off);
            Assert.Equal("SW2BRN", pair.On);
            Assert.Equal(0, pair.Flag);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateSwitchesLumpForPairs_KeepsPartnerAndWritesLump()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildSwitchResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "SW1BRN", "FLAT1");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateSwitchesLumpForPairs = true,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // Only the used switch and its partner survive, alongside the used flat.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Equal(
                new[] { "SW1BRN", "SW2BRN" },
                textures.Textures.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.NotNull(wad.FindFirst("FLAT1"));

            // One 20-byte entry: off texture, on texture and an int16 flag of 0.
            Assert.Equal(1, wad.Lumps.Count(l => l.Name == "SWITCHES"));
            byte[] switchesLump = wad.FindFirst("SWITCHES")!.ReadAll();
            Assert.Equal(20, switchesLump.Length);
            var entry = ReadSwitchEntry(switchesLump, 0);
            Assert.Equal("SW1BRN", entry.Off);
            Assert.Equal("SW2BRN", entry.On);
            Assert.Equal(0, entry.Flag);

            // No animation lump was requested: nothing is generated.
            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Null(wad.FindFirst("ANIMDEFS"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimatedLump_SkipsNonConsecutiveWallRunWithWarning()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            // BIGDOOR2 is NOT right after BIGDOOR1 in TEXTURE1: JUNK1 sits between them.
            BuildResourceWadWithGap(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "FLAT1");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimatedLumpForRuns = true,
                    AnimatedPrefixes = "BIGDOOR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // A non-consecutive run would make Odamex/Boom animate every texture between the
            // start and end frames (or overflow their fixed 32-frame arrays), so it is omitted.
            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Contains(result.Warnings, w => w.Contains("BIGDOOR1") && w.Contains("no queda consecutiva"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimatedLump_SkipsFlatRunWithForeignLumpBetweenFrames()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            // DUMMY1 is a flat name sitting between WATR1 and WATR2 inside F_START..F_END,
            // so the run is not a consecutive lump span in the merged output.
            BuildResourceWadWithGapFlat(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "TEX1", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimatedLumpForRuns = true,
                    AnimatedPrefixes = "WATR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Contains(result.Warnings, w => w.Contains("WATR1") && w.Contains("no queda consecutiva"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimatedLumpAndAnimdefs_SkipRunLongerThan32FramesWithWarning()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            // 40 consecutive frames: engines cap each animation at 32 frames.
            BuildResourceWadWithLongRun(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "B3", "FLAT1");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimdefsForAnimatedRuns = true,
                    GenerateAnimatedLumpForRuns = true,
                    AnimatedPrefixes = "B",
                    MaxAnimatedTextureFrames = 60,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Null(wad.FindFirst("ANIMDEFS"));
            Assert.Contains(result.Warnings, w => w.Contains("B1") && w.Contains("32"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimatedLump_DeclaresOnlyRunsOfAllowedPrefixes()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimatedLumpForRuns = true,
                    // Only the WATR flat run animates; BIGDOOR is a variant set and stays static.
                    AnimatedPrefixes = "WATR",
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            // The BIGDOOR run is still imported (variant set), but it is not declared.
            var textures = TextureSet.Read(wad.FindLast("TEXTURE1")!.ReadAll());
            Assert.Contains(textures.Textures, t => t.Name == "BIGDOOR7");

            // Only the flat entry is in the generated binary ANIMATED lump.
            byte[] animated = wad.FindFirst("ANIMATED")!.ReadAll();
            Assert.Equal(24, animated.Length);
            var flat = ReadAnimatedEntry(animated, 0);
            Assert.Equal(0, flat.IsTexture);
            Assert.Equal("WATR4", flat.EndName);
            Assert.Equal("WATR1", flat.StartName);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("prefijos"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GenerateAnimatedLump_EmptyPrefixes_DeclaresNothingWithWarning()
    {
        string dir = TempDir();
        try
        {
            string resourceWad = Path.Combine(dir, "resources.wad");
            string mapWad = Path.Combine(dir, "map.wad");
            string outputWad = Path.Combine(dir, "out.wad");

            BuildAnimatedResourceWad(resourceWad);
            BuildRunMapWad(mapWad, "MAP05", "BIGDOOR3", "WATR2");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { mapWad },
                ResourceWadPaths = new[] { resourceWad },
                OutputPath = outputWad,
                MapAssignments = new[] { new MapAssignment(mapWad, "MAP05", "MAP05") },
                Options = new MergeOptions
                {
                    AutoAssignMaps = false,
                    FilterToUsedResources = true,
                    GenerateAnimatedLumpForRuns = true,
                    MaxAnimatedTextureFrames = 8,
                    AnimatedRunTics = 8,
                },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(outputWad);

            Assert.Null(wad.FindFirst("ANIMATED"));
            Assert.Null(wad.FindFirst("ANIMDEFS"));
            Assert.Contains(result.Warnings, w => w.Contains("prefijos"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Builders
    // ------------------------------------------------------------------

    private static void BuildClassicMapWad(string path, string mapName)
    {
        var builder = new WadBuilder();
        builder.AddLump(mapName, new byte[1]);
        builder.AddLump("THINGS", new byte[10]);

        // 2 sidedefs: first uses TEX1/TEXMID, second uses "-" (no texture).
        builder.AddLump("LINEDEFS", new byte[14]);
        builder.AddLump("SIDEDEFS", MakeSidedefs(
            ("TEX1", "", "TEXMID"),
            ("-", "", "")));
        builder.AddLump("VERTEXES", new byte[4]);
        builder.AddLump("SEGS", new byte[12]);
        builder.AddLump("SSECTORS", new byte[4]);
        builder.AddLump("NODES", new byte[28]);
        builder.AddLump("SECTORS", MakeSectors(("FLAT1", "F_SKY1")));
        builder.AddLump("REJECT", new byte[0]);
        builder.AddLump("BLOCKMAP", new byte[2]);
        builder.Write(path);
    }

    /// <summary>Minimal classic map whose geometry uses one wall texture and one flat,
    /// optionally a second wall texture.</summary>
    private static void BuildRunMapWad(string path, string mapName, string wallName, string flatName, string? secondWallName = null)
    {
        var builder = new WadBuilder();
        builder.AddLump(mapName, new byte[1]);
        builder.AddLump("THINGS", new byte[10]);
        builder.AddLump("LINEDEFS", new byte[14]);
        builder.AddLump("SIDEDEFS", secondWallName is null
            ? MakeSidedefs((wallName, "", wallName))
            : MakeSidedefs((wallName, "", wallName), (secondWallName, "", secondWallName)));
        builder.AddLump("VERTEXES", new byte[4]);
        builder.AddLump("SEGS", new byte[12]);
        builder.AddLump("SSECTORS", new byte[4]);
        builder.AddLump("NODES", new byte[28]);
        builder.AddLump("SECTORS", MakeSectors((flatName, "F_SKY1")));
        builder.AddLump("REJECT", new byte[0]);
        builder.AddLump("BLOCKMAP", new byte[2]);
        builder.Write(path);
    }

    /// <summary>
    /// Resource WAD with an animation as consecutive numbered names (no ANIMATED/ANIMDEFS
    /// lump): BIGDOOR1..7 wall textures, SHAWN1..12 wall textures (a long variant cache),
    /// and WATR1..4 flats, plus an unused numbered flat (P1) and PLAYPAL.
    /// </summary>
    private static void BuildAnimatedResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("P1"));
        var textures = new List<TextureDef>();
        for (int i = 1; i <= 7; i++)
            textures.Add(MakeTexture($"BIGDOOR{i}", 64, 64, (0, 0, 0)));
        for (int i = 1; i <= 12; i++)
            textures.Add(MakeTexture($"SHAWN{i}", 64, 64, (0, 0, 0)));
        builder.AddLump("TEXTURE1", MakeTexture1(textures.ToArray()));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("P1", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        for (int i = 1; i <= 4; i++)
            builder.AddLump($"WATR{i}", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    private static void BuildResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("PATCHA", "PATCHB"));
        builder.AddLump("TEXTURE1", MakeTexture1(
            MakeTexture("TEX1", 64, 64, (0, 0, 0)),
            MakeTexture("TEX2", 64, 64, (1, 0, 0))));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("PATCHA", new byte[8]);
        builder.AddLump("PATCHB", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("FLAT2", new byte[4096]);
builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Like <see cref="BuildAnimatedResourceWad"/> but the WAD already ships its own
    /// ANIMDEFS lump (whose text must be preserved when generating runs).</summary>
    private static void BuildAnimdefsPlusRunsResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("P1"));
        var textures = new List<TextureDef>();
        for (int i = 1; i <= 7; i++)
            textures.Add(MakeTexture($"BIGDOOR{i}", 64, 64, (0, 0, 0)));
        builder.AddLump("TEXTURE1", MakeTexture1(textures.ToArray()));
        builder.AddLump("ANIMDEFS", System.Text.Encoding.ASCII.GetBytes("animatedTexture TEX99A, TEX99B, 8\n"));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("P1", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        for (int i = 1; i <= 4; i++)
            builder.AddLump($"WATR{i}", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Resource WAD with BIGDOOR1..7 and SW1BRN/SW2BRN textures, plus existing binary
    /// ANIMATED (one entry + 0xFF) and SWITCHES (one entry) lumps that must be preserved when
    /// generating new entries.</summary>
    private static void BuildBinaryLumpsResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("P1", "PATCHA", "PATCHB", "PATCHC"));
        var textures = new List<TextureDef>();
        for (int i = 1; i <= 7; i++)
            textures.Add(MakeTexture($"BIGDOOR{i}", 64, 64, (0, 0, 0)));
        textures.Add(MakeTexture("SW1BRN", 64, 64, (1, 0, 0)));
        textures.Add(MakeTexture("SW2BRN", 64, 64, (2, 0, 0)));
        builder.AddLump("TEXTURE1", MakeTexture1(textures.ToArray()));
        builder.AddLump("ANIMATED", BuildAnimatedBytes((1, "BIGDOOR9", "BIGDOOR8", 4)));
        builder.AddLump("SWITCHES", BuildSwitchesBytes(("SW1PAD", "SW2PAD", 0)));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("P1", new byte[8]);
        builder.AddLump("PATCHA", new byte[8]);
        builder.AddLump("PATCHB", new byte[8]);
        builder.AddLump("PATCHC", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Resource WAD with a switch pair (SW1BRN/SW2BRN), one unused texture and FLAT1.</summary>
    private static void BuildSwitchResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("PATCHA", "PATCHB", "PATCHC"));
        builder.AddLump("TEXTURE1", MakeTexture1(
            MakeTexture("SW1BRN", 64, 64, (0, 0, 0)),
            MakeTexture("SW2BRN", 64, 64, (1, 0, 0)),
            MakeTexture("TEX1", 64, 64, (2, 0, 0))));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("PATCHA", new byte[8]);
        builder.AddLump("PATCHB", new byte[8]);
        builder.AddLump("PATCHC", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Resource WAD with an animated wall run that is NOT consecutive in TEXTURE1:
    /// JUNK1 sits between BIGDOOR1 and BIGDOOR2.</summary>
    private static void BuildResourceWadWithGap(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("P1"));
        builder.AddLump("TEXTURE1", MakeTexture1(
            MakeTexture("BIGDOOR1", 64, 64, (0, 0, 0)),
            MakeTexture("JUNK1", 64, 64, (0, 0, 0)),
            MakeTexture("BIGDOOR2", 64, 64, (0, 0, 0)),
            MakeTexture("BIGDOOR3", 64, 64, (0, 0, 0)),
            MakeTexture("BIGDOOR4", 64, 64, (0, 0, 0))));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("P1", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Resource WAD with a flat run where an unrelated flat (DUMMY1) sits between
    /// WATR1 and WATR2 inside F_START..F_END.</summary>
    private static void BuildResourceWadWithGapFlat(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("P1"));
        builder.AddLump("TEXTURE1", MakeTexture1(
            MakeTexture("TEX1", 64, 64, (0, 0, 0))));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("P1", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("WATR1", new byte[4096]);
        builder.AddLump("DUMMY1", new byte[4096]);
        builder.AddLump("WATR2", new byte[4096]);
        builder.AddLump("WATR3", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Resource WAD with a 40-frame consecutive wall run (B1..B40). Names stay
    /// within the 8-character engine limit.</summary>
    private static void BuildResourceWadWithLongRun(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("P1"));
        var textures = new List<TextureDef>();
        for (int i = 1; i <= 40; i++)
            textures.Add(MakeTexture($"B{i}", 64, 64, (0, 0, 0)));
        builder.AddLump("TEXTURE1", MakeTexture1(textures.ToArray()));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("P1", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }
    /// entries followed by the standard 0xFF terminator.</summary>
    private static byte[] BuildAnimatedBytes(params (byte IsTexture, string Last, string First, int Speed)[] entries)
    {
        var ms = new MemoryStream();
        foreach (var (isTexture, last, first, speed) in entries)
        {
            ms.WriteByte(isTexture);
            WriteNameField(ms, last);
            WriteNameField(ms, first);
            ms.WriteByte((byte)(speed & 0xFF));
            ms.WriteByte((byte)((speed >> 8) & 0xFF));
            ms.WriteByte((byte)((speed >> 16) & 0xFF));
            ms.WriteByte((byte)((speed >> 24) & 0xFF));
        }
        ms.WriteByte(0xFF);
        return ms.ToArray();
    }

    /// <summary>Writes a binary SWITCHES lump from (off texture, on texture, flag) entries.</summary>
    private static byte[] BuildSwitchesBytes(params (string Off, string On, short Flag)[] entries)
    {
        var ms = new MemoryStream();
        foreach (var (off, on, flag) in entries)
        {
            WriteNameField(ms, off);
            WriteNameField(ms, on);
            ms.WriteByte((byte)(flag & 0xFF));
            ms.WriteByte((byte)((flag >> 8) & 0xFF));
        }
        return ms.ToArray();
    }

    /// <summary>Writes a name as a 9-byte null-padded field (ANIMATED/SWITCHES record layout).</summary>
    private static void WriteNameField(Stream ms, string name)
    {
        byte[] field = new byte[9];
        byte[] src = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(src, field, Math.Min(src.Length, field.Length));
        ms.Write(field, 0, field.Length);
    }

    /// <summary>Parses one 23-byte ANIMATED entry: istexture, endname (last frame), startname (first
    /// frame) and the little-endian speed (tics).</summary>
    private static (byte IsTexture, string EndName, string StartName, int Speed) ReadAnimatedEntry(byte[] data, int index)
    {
        int o = index * 23;
        byte isTexture = data[o];
        string end = ReadName(data, o + 1);
        string start = ReadName(data, o + 10);
        int speed = data[o + 19] | (data[o + 20] << 8) | (data[o + 21] << 16) | (data[o + 22] << 24);
        return (isTexture, end, start, speed);
    }

    /// <summary>Parses one 20-byte SWITCHES entry: off texture, on texture and the int16 flag.</summary>
    private static (string Off, string On, short Flag) ReadSwitchEntry(byte[] data, int index)
    {
        int o = index * 20;
        string off = ReadName(data, o);
        string on = ReadName(data, o + 9);
        short flag = (short)(data[o + 18] | (data[o + 19] << 8));
        return (off, on, flag);
    }

    private static string ReadName(byte[] data, int offset)
    {
        int end = offset;
        while (end < data.Length && end < offset + 9 && data[end] != 0)
            end++;
        return System.Text.Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static string NormalizeWs(string s)
        => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    private static void BuildAnimdefsResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("PATCHA", "PATCHB", "PATCHC"));
        builder.AddLump("TEXTURE1", MakeTexture1(
            MakeTexture("TEX1", 64, 64, (0, 0, 0)),
            MakeTexture("SW1BRCO", 64, 64, (1, 0, 0)),
            MakeTexture("SW2BRCO", 64, 64, (2, 0, 0))));
        builder.AddLump("ANIMDEFS", System.Text.Encoding.ASCII.GetBytes(
            "animatedTexture SW1BRCO, SW2BRCO, 8, RANDOM, 1.0, 0.5"));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("PATCHA", new byte[8]);
        builder.AddLump("PATCHB", new byte[8]);
        builder.AddLump("PATCHC", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("FLAT2", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    /// <summary>Resource WAD with sprites, status-bar and font groups plus the flats used by the map.</summary>
    private static void BuildSpriteResourceWad(string path)
    {
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames("PATCHA"));
        builder.AddLump("TEXTURE1", MakeTexture1(
            MakeTexture("TEX1", 64, 64, (0, 0, 0))));
        builder.AddLump("P_START", new byte[0]);
        builder.AddLump("PATCHA", new byte[8]);
        builder.AddLump("P_END", new byte[0]);
        builder.AddLump("F_START", new byte[0]);
        builder.AddLump("FLAT1", new byte[4096]);
        builder.AddLump("F_END", new byte[0]);
        builder.AddLump("S_START", new byte[0]);
        builder.AddLump("TROOA1", new byte[8]);
        builder.AddLump("S_END", new byte[0]);
        builder.AddLump("ST_START", new byte[0]);
        builder.AddLump("STBAR", new byte[8]);
        builder.AddLump("STGNUM0", new byte[8]);
        builder.AddLump("ST_END", new byte[0]);
        builder.AddLump("FM_START", new byte[0]);
        builder.AddLump("FONTA", new byte[8]);
        builder.AddLump("FM_END", new byte[0]);
        builder.AddLump("PLAYPAL", new byte[768]);
        builder.Write(path);
    }

    private static byte[] MakeSidedefs(params (string Top, string Bottom, string Middle)[] sides)
    {
        var ms = new MemoryStream();
        foreach (var (top, bottom, middle) in sides)
        {
            ms.Write(new byte[4], 0, 4);
            WriteName(ms, top);
            WriteName(ms, bottom);
            WriteName(ms, middle);
            ms.Write(new byte[2], 0, 2);
        }
        return ms.ToArray();
    }

    private static byte[] MakeSectors(params (string Floor, string Ceiling)[] sectors)
    {
        var ms = new MemoryStream();
        foreach (var (floor, ceiling) in sectors)
        {
            WriteName(ms, floor);
            WriteName(ms, ceiling);
            ms.Write(new byte[6], 0, 6);
        }
        return ms.ToArray();
    }

    /// <summary>26-byte SECTORS records with extra leading fields: textures at +4/+12.</summary>
    private static byte[] MakeExtendedSectors(params (string Floor, string Ceiling)[] sectors)
    {
        var ms = new MemoryStream();
        foreach (var (floor, ceiling) in sectors)
        {
            ms.Write(new byte[4], 0, 4);
            WriteName(ms, floor);
            WriteName(ms, ceiling);
            ms.Write(new byte[6], 0, 6);
        }
        return ms.ToArray();
    }

    private static void WriteName(Stream ms, string name)
    {
        byte[] field = WadNames.ToWadField(name);
        ms.Write(field, 0, field.Length);
    }

    private static byte[] MakePnames(params string[] names)
    {
        var list = new PnamesList();
        list.Names.AddRange(names);
        return list.Write();
    }

    private static byte[] MakeTexture1(params TextureDef[] defs)
    {
        var set = new TextureSet();
        set.Textures.AddRange(defs);
        return set.Write();
    }

    private static TextureDef MakeTexture(string name, int width, int height, params (int PatchIndex, int X, int Y)[] patches)
        => new()
        {
            Name = name,
            Masked = 0,
            Width = (short)width,
            Height = (short)height,
            Patches = patches.Select(p => new PatchRef(p.X, p.Y, p.PatchIndex)).ToList(),
        };

    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"cwc-{Guid.NewGuid():N}");
}