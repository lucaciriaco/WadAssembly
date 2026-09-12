using WadAssembly.Core.WadFormat;
using Xunit;

namespace WadAssembly.Tests;

public class WadRoundtripTests
{
    private static string TempWad(string name)
        => Path.Combine(Path.GetTempPath(), $"cwc-{Guid.NewGuid():N}-{name}");

    [Fact]
    public void Builder_RoundTripsLumpsAndOrder()
    {
        string path = TempWad("roundtrip.wad");
        try
        {
            var builder = new WadBuilder();
            builder.AddLump("MAP01", new byte[] { 1, 2, 3 });
            builder.AddLump("ENDOOM", new byte[4] { 9, 8, 7, 6 });
            builder.AddLump("FLAT1", new byte[] { 255, 0 });
            builder.Write(path);

            using var wad = WadFile.Open(path);

            Assert.Equal(WadType.PWad, wad.WadType);
            Assert.Equal(3, wad.Lumps.Count);
            Assert.Equal("MAP01", wad.Lumps[0].Name);
            Assert.Equal("ENDOOM", wad.Lumps[1].Name);
            Assert.Equal("FLAT1", wad.Lumps[2].Name);
            Assert.Equal(new byte[] { 1, 2, 3 }, wad.Lumps[0].ReadAll());
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, wad.Lumps[1].ReadAll());
            Assert.Equal(new byte[] { 255, 0 }, wad.Lumps[2].ReadAll());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_ThrowsOnNonWad()
    {
        string path = TempWad("notawad.wad");
        try
        {
            File.WriteAllText(path, "This is definitely not a wad file content");
            Assert.Throws<WadException>(() => WadFile.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FindLast_FindsLastOccurrence()
    {
        string path = TempWad("dupes.wad");
        try
        {
            var builder = new WadBuilder();
            builder.AddLump("TEX1", new byte[] { 1 });
            builder.AddLump("TEX2", new byte[] { 2 });
            builder.AddLump("TEX1", new byte[] { 3 });
            builder.Write(path);

            using var wad = WadFile.Open(path);
            Assert.Equal(new byte[] { 3 }, wad.FindLast("TEX1")!.ReadAll());
            Assert.Equal(new byte[] { 1 }, wad.FindFirst("tex1")!.ReadAll());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Names_AreNormalizedToEightCharsAndUpper()
    {
        string path = TempWad("names.wad");
        try
        {
            var builder = new WadBuilder();
            builder.AddLump("A", new byte[1]);
            builder.AddLump("LONGNAME123", new byte[1]);
            builder.Write(path);

            using var wad = WadFile.Open(path);
            Assert.Equal("A", wad.Lumps[0].Name);
            Assert.Equal("LONGNAME", wad.Lumps[1].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}