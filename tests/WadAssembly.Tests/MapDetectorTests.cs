using WadAssembly.Core.Maps;
using Xunit;
using WadAssembly.Core.WadFormat;

namespace WadAssembly.Tests;

public class MapDetectorTests
{
    private static WadFile Build(params string[] names)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cwc-{Guid.NewGuid():N}-detect.wad");
        var builder = new WadBuilder();
        foreach (string name in names)
            builder.AddLump(name, new byte[] { 0xAA });
        builder.Write(path);
        return WadFile.Open(path);
    }

    [Fact]
    public void DetectsDoom2Map()
    {
        using var wad = Build("MAP01", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
            "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP", "P_START", "PATCH1", "P_END");
        var maps = MapDetector.DetectMaps(wad);

        var map = Assert.Single(maps);
        Assert.Equal("MAP01", map.OriginalName);
        Assert.False(map.IsUdmf);
        Assert.Equal(11, map.LumpCount); // marker + 10 body lumps
    }

    [Fact]
    public void DetectsMultipleClassicMaps()
    {
        using var wad = Build("MAP01", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
            "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP",
            "MAP02", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
            "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP");
        var maps = MapDetector.DetectMaps(wad);

        Assert.Equal(2, maps.Count);
        Assert.Equal("MAP01", maps[0].OriginalName);
        Assert.Equal("MAP02", maps[1].OriginalName);
        Assert.Equal(11, maps[0].LumpCount);
    }

    [Fact]
    public void DetectsDoom1Maps()
    {
        using var wad = Build("E1M1", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
            "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP",
            "E2M4", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
            "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP");
        var maps = MapDetector.DetectMaps(wad);

        Assert.Equal(2, maps.Count);
        Assert.Equal("E1M1", maps[0].OriginalName);
        Assert.Equal("E2M4", maps[1].OriginalName);
    }

    [Fact]
    public void DetectsUdmfMap()
    {
        using var wad = Build("MAP01", "TEXTMAP", "ENDMAP", "BEHAVIOR", "REJECT", "BLOCKMAP",
            "MAP02", "TEXTMAP", "ENDMAP");
        var maps = MapDetector.DetectMaps(wad);

        Assert.Equal(2, maps.Count);
        Assert.True(maps[0].IsUdmf);
        Assert.True(maps[1].IsUdmf);
        Assert.Equal(6, maps[0].LumpCount); // MAP01 + 5 body lumps
        Assert.Equal(3, maps[1].LumpCount); // MAP02 + TEXTMAP + ENDMAP + nothing else
    }

    [Fact]
    public void IgnoresNonMapLumps()
    {
        using var wad = Build("P_START", "PATCH1", "P_END", "F_START", "FLAT1", "F_END",
            "TEXTURE1", "PLAYPAL", "MAP01", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES",
            "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP");
        var maps = MapDetector.DetectMaps(wad);

        var map = Assert.Single(maps);
        Assert.Equal("MAP01", map.OriginalName);
    }
}