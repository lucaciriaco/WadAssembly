using System.Text;
using CommunityWadCompiler.Core.Merge;
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
                    new MapAssignment(map1, "MAP01", "MAP01", "Hangar de la Arena"),
                    new MapAssignment(map2, "MAP07", "MAP02", "Las Torres Gemelas"),
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
            Assert.Contains("map MAP02 \"Las Torres Gemelas\"", text);
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

    // ------------------------------------------------------------------
    // Builders
    // ------------------------------------------------------------------

    private static string BuildClassicMapWad(string dir, string fileName, string header, byte[]? extraLump = null)
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
        builder.Write(path);
        return path;
    }

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
}