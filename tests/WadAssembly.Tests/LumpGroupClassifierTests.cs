using WadAssembly.Core.WadFormat;
using Xunit;

namespace WadAssembly.Tests;

public class LumpGroupClassifierTests
{
    private static Lump[] Build(params string[] names)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cwc-{Guid.NewGuid():N}-classify.wad");
        var builder = new WadBuilder();
        foreach (string name in names)
            builder.AddLump(name, new byte[] { 0xAA });
        builder.Write(path);
        using var wad = WadFile.Open(path);
        return wad.Lumps.ToArray();
    }

    [Fact]
    public void ClassifiesSpritesFlatsPatchesAndUnmarked()
    {
        var lumps = Build("XMARKER", "S_START", "SHTGA0", "WEPA1", "S_END",
            "F_START", "FLOOR4_8", "F_END",
            "P_START", "WALL1", "P_END",
            "TITLEPIC");

        var cats = LumpGroupClassifier.ClassifyAll(lumps);

        Assert.Equal(LumpCategory.Other, cats[0]);    // XMARKER (unmarked)
        Assert.Equal(LumpCategory.Sprite, cats[1]);    // S_START
        Assert.Equal(LumpCategory.Sprite, cats[2]);    // SHTGA0
        Assert.Equal(LumpCategory.Sprite, cats[3]);    // WEPA1
        Assert.Equal(LumpCategory.Sprite, cats[4]);    // S_END
        Assert.Equal(LumpCategory.Flat, cats[5]);      // F_START
        Assert.Equal(LumpCategory.Flat, cats[6]);      // FLOOR4_8
        Assert.Equal(LumpCategory.Flat, cats[7]);      // F_END
        Assert.Equal(LumpCategory.Texture, cats[8]);   // P_START
        Assert.Equal(LumpCategory.Texture, cats[9]);   // WALL1
        Assert.Equal(LumpCategory.Texture, cats[10]);  // P_END
        Assert.Equal(LumpCategory.Other, cats[11]);    // TITLEPIC (unmarked)
    }

    [Fact]
    public void HandlesUnterminatedGroupByClassifyingToEndOfWad()
    {
        var lumps = Build("F_START", "FLAT1", "FLAT2");

        var cats = LumpGroupClassifier.ClassifyAll(lumps);

        Assert.Equal(LumpCategory.Flat, cats[0]);
        Assert.Equal(LumpCategory.Flat, cats[1]);
        Assert.Equal(LumpCategory.Flat, cats[2]);
    }

    [Fact]
    public void IsMarkerRecognizesStartAndEndNames()
    {
        Assert.True(LumpGroupClassifier.IsMarker("S_START"));
        Assert.True(LumpGroupClassifier.IsMarker("S_END"));
        Assert.True(LumpGroupClassifier.IsMarker("SS_START"));
        Assert.True(LumpGroupClassifier.IsMarker("SS_END"));
        Assert.True(LumpGroupClassifier.IsMarker("F_START"));
        Assert.True(LumpGroupClassifier.IsMarker("F_END"));
        Assert.True(LumpGroupClassifier.IsMarker("T_END"));
        Assert.False(LumpGroupClassifier.IsMarker("SHTGA0"));
        Assert.False(LumpGroupClassifier.IsMarker("FLOOR4_8"));
        Assert.False(LumpGroupClassifier.IsMarker("WALL1"));
    }
}