using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Merge;
using CommunityWadCompiler.Core.Textures;
using CommunityWadCompiler.Core.WadFormat;
using Xunit;

namespace CommunityWadCompiler.Tests;

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