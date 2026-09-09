using System.Text;
using CommunityWadCompiler.Core;
using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Merge;
using CommunityWadCompiler.Core.Music;
using CommunityWadCompiler.Core.Textures;
using CommunityWadCompiler.Core.WadFormat;
using Xunit;

namespace CommunityWadCompiler.Tests;

public class MapInfoTests
{
    [Fact]
    public void MergeWithLevelNames_GeneratesMapInfoLump()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string map2 = BuildClassicMapWad(dir, "m2.wad", "MAP07");
            string output = Path.Combine(dir, "out.wad");

var request = new MergeRequest
            {
                InputWadPaths = new[] { map1, map2 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Hangar de la Arena", "CITY1"),
                    new MapAssignment(map2, "MAP07", "MAP02", "Las Torres Gemelas", "MUSW1"),
                },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            Lump? mapinfo = wad.FindFirst("MAPINFO");
            Assert.NotNull(mapinfo);
            string text = Encoding.UTF8.GetString(mapinfo!.ReadAll());

            Assert.Contains("map MAP01 \"Hangar de la Arena\"", text);
            Assert.Contains("music = \"CITY1\"", text);
            Assert.Contains("map MAP02 \"Las Torres Gemelas\"", text);
            Assert.Contains("music = \"MUSW1\"", text);

            // ZDoom/Odamex brace blocks must open AND close with } on their own line.
            Assert.Matches(@"map MAP01 ""Hangar de la Arena""\r?\n\{[\s\S]*?\}\r?\n", text);
            Assert.Matches(@"map MAP02 ""Las Torres Gemelas""\r?\n\{[\s\S]*?\}\r?\n", text);
            Assert.Matches(@"\}\s*$", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Merge_AssignmentsOutOfWadOrder_EmitMapInfoInSlotOrder()
    {
        string dir = TempDir();
        try
        {
            // WAD A carries the map that becomes MAP01 but is listed AFTER WAD B in inputs.
            string wadA = BuildClassicMapWad(dir, "a.wad", "MAP03");
            string wadB = BuildClassicMapWad(dir, "b.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { wadA, wadB },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(wadA, "MAP03", "MAP01", "Arena"),
                    new MapAssignment(wadB, "MAP01", "MAP02", "Tower"),
                },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            Assert.Contains("map MAP01 \"Arena\"", Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll()));
            Assert.Contains("map MAP02 \"Tower\"", Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll()));

            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.True(
                text.IndexOf("map MAP01", StringComparison.Ordinal)
                < text.IndexOf("map MAP02", StringComparison.Ordinal),
                "El MAPINFO debe listar los mapas en orden de slot (MAP01 antes que MAP02)");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithoutLevelNames_OmitsMapInfo()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            Assert.Null(wad.FindFirst("MAPINFO"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithNames_ReplacesExistingInputMapInfo()
    {
        string dir = TempDir();
        try
        {
            // Input WAD already carries a foreign MAPINFO lump.
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01",
                Encoding.ASCII.GetBytes("map MAP01 { music = \"D_NOWHERE\" }"));

            string output = Path.Combine(dir, "out.wad");
            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01", "El Nido") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            var infos = wad.Lumps.Where(l => l.Name == "MAPINFO").ToList();
            Assert.Single(infos); // ours replaces the input one
            string text = Encoding.UTF8.GetString(infos[0].ReadAll());
            Assert.Contains("map MAP01 \"El Nido\"", text);
            Assert.DoesNotContain("D_NOWHERE", text);
            Assert.Contains(result.Warnings, w => w.Contains("MAPINFO", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithMusicLumpInInputWad_CopiesItAndWritesMapInfo()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01", musicLumpName: "CITY1");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01", null, "CITY1") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            Assert.NotNull(wad.FindFirst("CITY1"));
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("map MAP01 \"\"", text);
            Assert.Contains("music = \"CITY1\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithMusicInFilteredResourceWad_CopiesLumpExplicitly()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string res = BuildResourceWad(dir, "res.wad", musicLumpName: "RSCMUS");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                ResourceWadPaths = new[] { res },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01", "El Nido", "RSCMUS") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = true },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            Assert.NotNull(wad.FindFirst("RSCMUS")); // copied despite resource filtering
            Assert.Equal(1, result.MusicCopied);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("map MAP01 \"El Nido\"", text);
            Assert.Contains("music = \"RSCMUS\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithMissingMusic_WarnsButReferencesIt()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01", null, "D_FAKE") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Contains(result.Warnings, w => w.Contains("D_FAKE"));
            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("music = \"D_FAKE\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithDuplicateMusicNames_RenamesSecondWad()
    {
        string dir = TempDir();
        try
        {
            // Both WADs carry a music lump with the same name "CITY1".
            string map1 = BuildClassicMapWad(dir, "one.wad", "MAP01", musicLumpName: "CITY1");
            string map2 = BuildClassicMapWad(dir, "two.wad", "MAP02", musicLumpName: "CITY1");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1, map2 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", null, "CITY1"),
                    new MapAssignment(map2, "MAP02", "MAP02", null, "CITY_TWO"),
                },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            Assert.NotNull(wad.FindFirst("CITY1"));      // first source keeps the base name
            Assert.NotNull(wad.FindFirst("CITY_TWO"));   // second one renamed (8-char cap)
            Assert.Equal(1, result.MusicCopied);
            Assert.Contains(result.Info, i => i.Contains("CITY1") && i.Contains("CITY_TWO"));

            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("music = \"CITY1\"", text);
            Assert.Contains("music = \"CITY_TWO\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RenameDuplicate_StaysWithinEightCharacters()
    {
        var used = new HashSet<string>(StringComparer.Ordinal) { "D_RUNNIN" };
        string renamed = MusicLumpDetector.RenameDuplicate("D_RUNNIN", @"C:\wads\map04.wad", used);
        Assert.True(renamed.Length <= 8, $"'{renamed}' supera los 8 caracteres");
        Assert.DoesNotContain(renamed, used);
        Assert.Contains("MAP0", renamed); // tag remains recognizable
        Assert.DoesNotContain("D_RUNNIN", renamed);
    }

    [Fact]
    public void GeneratedMapInfo_HasVersionAndCompileTimestamp()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01", "Hangar") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.DoesNotContain("// Proyecto:", text);
            Assert.Contains($"// Versión: {CompilerInfo.Version}.", text);
            Assert.Matches(@"// Versión: 1\.0\.0\.\d{6}", text);
            Assert.Matches(@"// Compilado: \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ProjectNameAndEditableVersionPrefix_WrittenToMapInfo()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                ProjectName = "La Hermandad de la Arena",
                VersionPrefix = "2.7",
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[] { new MapAssignment(map1, "MAP01", "MAP01", "Hangar") },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("// Proyecto: La Hermandad de la Arena", text);
            Assert.Matches(@"// Versión: 2\.7\.\d{6}", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithStatusAndModified_WritesCommentsBeforeMap()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Hangar", null, "John Doe", "WIP", "05/09/2026 14:30"),
                },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            int mapBlock = text.IndexOf("map MAP01", StringComparison.Ordinal);
            Assert.True(mapBlock > 0, "Debe existir el bloque del mapa");
            Assert.True(text.IndexOf("// Estado: WIP", StringComparison.Ordinal) < mapBlock);
            Assert.True(text.IndexOf("// Última modificación: 05/09/2026 14:30", StringComparison.Ordinal) < mapBlock);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithAuthor_WritesCommentBeforeMap()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Hangar", null, "John Doe"),
                },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("// Autor: John Doe", text);
            Assert.True(
                text.IndexOf("// Autor: John Doe", StringComparison.Ordinal)
                < text.IndexOf("map MAP01", StringComparison.Ordinal),
                "El comentario debe ir antes del bloque del mapa");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeWithAuthorOnly_WritesCommentAndMapBlock()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", null, null, "Solo Autor"),
                },
                Options = new MergeOptions { AutoAssignMaps = false, FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("// Autor: Solo Autor", text);
            Assert.Contains("map MAP01 \"\"", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_ZdoomBraceFormat_ExtractsNamesMusicAndSky()
    {
        const string text = """
            map MAP01
            {
                levelname = "Entryway"
                music = "D_RUNNIN"
                sky1 = "SKY1"
            }
            map MAP02
            {
                levelname = "Tower"
                music = "D_E1M2"
            }
            """;

        var parsed = MapInfoParser.Parse(text);

        Assert.True(parsed.TryGetValue("MAP01", out var m1));
        Assert.Equal("Entryway", m1.LevelName);
        Assert.Equal("D_RUNNIN", m1.MusicName);
        Assert.Equal("SKY1", m1.SkyName);

        Assert.True(parsed.TryGetValue("MAP02", out var m2));
        Assert.Equal("Tower", m2.LevelName);
        Assert.Equal("D_E1M2", m2.MusicName);
    }

    [Fact]
    public void Parse_ZdoomInlineBraceAfterName_ExtractsProperties()
    {
        const string text = """
            map MAP01 "Hangar" { levelname = "Hangar"; music = "D_E1M1"; sky1 = "SKY1" }
            """;

        var parsed = MapInfoParser.Parse(text);

        Assert.True(parsed.TryGetValue("MAP01", out var m));
        Assert.Equal("Hangar", m.LevelName);
        Assert.Equal("D_E1M1", m.MusicName);
        Assert.Equal("SKY1", m.SkyName);
    }

    [Fact]
    public void Parse_OdamexUnquotedValues_ExtractsMusicAndSky()
    {
        const string text = """
            map MAP01
            {
                levelnum = 1
                levelname = "Entryway"
                music = D_RUNNIN
                sky1 = SKY1
            }
            """;

        var parsed = MapInfoParser.Parse(text);

        Assert.True(parsed.TryGetValue("MAP01", out var m));
        Assert.Equal("Entryway", m.LevelName);
        Assert.Equal("D_RUNNIN", m.MusicName);
        Assert.Equal("SKY1", m.SkyName);
    }

    [Fact]
    public void Parse_ClassicPropertiesWithoutBraces_StillExtracts()
    {
        const string text = """
            map MAP01 "Hangar"
            next MAP02
            par 30
            music D_RUNNIN
            sky1 SKY1
            cluster 1
            """;

        var parsed = MapInfoParser.Parse(text);

        Assert.True(parsed.TryGetValue("MAP01", out var m));
        Assert.Equal("Hangar", m.LevelName);
        Assert.Equal("D_RUNNIN", m.MusicName);
        Assert.Equal("SKY1", m.SkyName);
    }

    [Fact]
    public void Parse_ClosedBlockThenSection_DoesNotLeakPropertiesIntoMap()
    {
        const string text = """
            map MAP01
            {
                levelname = "Entryway"
                music = "D_RUNNIN"
            }
            cluster 1
            {
                exittext = "Done"
                music = "D_VICTOR"
            }
            """;

        var parsed = MapInfoParser.Parse(text);

        Assert.True(parsed.TryGetValue("MAP01", out var m));
        Assert.Equal("D_RUNNIN", m.MusicName);
    }

    // ------------------------------------------------------------------
    // Builders
    // ------------------------------------------------------------------

    private static string BuildClassicMapWad(string dir, string fileName, string header, byte[]? extraLump = null, string? musicLumpName = null)
    {
        string path = Path.Combine(dir, fileName);
        var builder = new WadBuilder();
        builder.AddLump(header, new byte[1]);
        builder.AddLump("THINGS", new byte[10]);
        builder.AddLump("LINEDEFS", new byte[14]);
        builder.AddLump("SIDEDEFS", MakeSidedefs(("TEX1", "", "TEXMID")));
        builder.AddLump("VERTEXES", new byte[4]);
        builder.AddLump("SEGS", new byte[12]);
        builder.AddLump("SSECTORS", new byte[4]);
        builder.AddLump("NODES", new byte[28]);
        builder.AddLump("SECTORS", MakeSectors(("FLAT1", "F_SKY1")));
        builder.AddLump("REJECT", new byte[0]);
        builder.AddLump("BLOCKMAP", new byte[2]);
        if (extraLump is not null)
            builder.AddLump("MAPINFO", extraLump);
        if (musicLumpName is not null)
            builder.AddLump(musicLumpName, FakeMusData());
        builder.Write(path);
        return path;
    }

    private static string BuildResourceWad(string dir, string fileName, string? musicLumpName = null)
    {
        string path = Path.Combine(dir, fileName);
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", new PnamesList().Write());
        builder.AddLump("TEXTURE1", new TextureSet().Write());
        if (musicLumpName is not null)
            builder.AddLump(musicLumpName, FakeMusData());
        builder.Write(path);
        return path;
    }

    private static byte[] FakeMusData() =>
        new byte[] { (byte)'M', (byte)'U', (byte)'S', 0x1A, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] MakeSidedefs(params (string Top, string Bottom, string Middle)[] sides)
    {
        var ms = new MemoryStream();
        foreach (var (top, bottom, middle) in sides)
        {
            ms.Write(new byte[4], 0, 4);
            ms.Write(WadNames.ToWadField(top));
            ms.Write(WadNames.ToWadField(bottom));
            ms.Write(WadNames.ToWadField(middle));
            ms.Write(new byte[2], 0, 2);
        }
        return ms.ToArray();
    }

    private static byte[] MakeSectors(params (string Floor, string Ceiling)[] sectors)
    {
        var ms = new MemoryStream();
        foreach (var (floor, ceiling) in sectors)
        {
            ms.Write(WadNames.ToWadField(floor));
            ms.Write(WadNames.ToWadField(ceiling));
            ms.Write(new byte[6], 0, 6);
        }
        return ms.ToArray();
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"cwc-mapinfo-{Guid.NewGuid():N}");

    [Fact]
    public void Merge_ExternalMusicFile_CopiesAndReferencesInMapInfo()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01", musicLumpName: "D_ORIG");
            string output = Path.Combine(dir, "out.wad");

            // External .it music file (minimal IMPM header).
            byte[] itData = Encoding.ASCII.GetBytes("IMPMexternal_song_padded_xxxxxxxx");
            string extPath = Path.Combine(dir, "my_track.it");
            File.WriteAllBytes(extPath, itData);

            var externalMusic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EXT01"] = extPath,
            };

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Test Map", "EXT01"),
                },
                ExternalMusicFiles = externalMusic,
                Options = new MergeOptions { AutoAssignMaps = false },
            };

            var result = new WadMerger().Merge(request);

            Assert.Equal(1, result.MusicCopied);

            using var wad = WadFile.Open(output);

            // Lump "EXT01" should exist with the external file bytes.
            Lump? extLump = wad.FindFirst("EXT01");
            Assert.NotNull(extLump);
            Assert.Equal(itData, extLump!.ReadAll());

            // MAPINFO should reference EXT01.
            string mapInfo = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("music = \"EXT01\"", mapInfo);

            // The original D_ORIG was NOT copied (it's assigned as EXT01, not D_ORIG).
            Assert.Null(wad.FindFirst("D_ORIG"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Merge_ExternalMusicFileMissing_LogsWarning()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var externalMusic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EXT01"] = Path.Combine(dir, "nonexistent.it"),
            };

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Test Map", "EXT01"),
                },
                ExternalMusicFiles = externalMusic,
                Options = new MergeOptions { AutoAssignMaps = false },
            };

            var result = new WadMerger().Merge(request);

            // MusicCopied stays 0 since the file was missing.
            Assert.Equal(0, result.MusicCopied);
            Assert.Contains(result.Warnings, i => i.Contains("no encontrado"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Merge_IntermissionMusic_CopiesLumpAndWritesGameInfoBlock()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01", musicLumpName: "D_INT");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Test Map"),
                },
                IntermissionMusic = "D_INT",
                Options = new MergeOptions { AutoAssignMaps = false },
            };

            var result = new WadMerger().Merge(request);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.True(result.MusicCopied >= 1);

            using var wad = WadFile.Open(output);

            // The intermission lump must exist in the output with the MUS bytes.
            Lump? intLump = wad.FindFirst("D_INT");
            Assert.NotNull(intLump);
            Assert.Equal(FakeMusData(), intLump!.ReadAll());

            // MAPINFO must reference it via the gameinfo block (ZDoom new format).
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("gameinfo", text);
            Assert.Contains("intermissionmusic = \"D_INT\"", text);
            Assert.Matches(@"gameinfo\r?\n\{(?<body>[\s\S]*?)intermissionmusic = ""D_INT""[\s\S]*?\}", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Merge_Sky2AndScroll_WritesBothLayersWithRotationSpeed()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Test Map",
                        SkyName: "SKY1", Sky2Name: "SKYFOG", SkyScroll: 0.5, Sky2Scroll: 1.25),
                },
                Options = new MergeOptions { AutoAssignMaps = false },
            };

            var result = new WadMerger().Merge(request);

            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());

            // Both layers with their own rotation speed as the ZDoom sky offset.
            Assert.Contains("sky1 = \"SKY1\", 0.5", text);
            Assert.Contains("sky2 = \"SKYFOG\", 1.25", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Merge_SkyScrollZero_OmitsRotationOffset()
    {
        string dir = TempDir();
        try
        {
            string map1 = BuildClassicMapWad(dir, "m1.wad", "MAP01");
            string output = Path.Combine(dir, "out.wad");

            var request = new MergeRequest
            {
                InputWadPaths = new[] { map1 },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(map1, "MAP01", "MAP01", "Test Map", SkyName: "SKY1"),
                },
                Options = new MergeOptions { AutoAssignMaps = false },
            };

            var result = new WadMerger().Merge(request);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            using var wad = WadFile.Open(output);
            string text = Encoding.UTF8.GetString(wad.FindFirst("MAPINFO")!.ReadAll());
            Assert.Contains("sky1 = \"SKY1\"", text);
            Assert.DoesNotContain("sky1 = \"SKY1\",", text);
            Assert.DoesNotContain("sky2", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}