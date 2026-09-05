using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Merge;
using Xunit;
using CommunityWadCompiler.Core.Textures;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Tests;

public class TextureMergeTests
{
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

    [Fact]
    public void MergesDedupedPatchesAndTextures()
    {
        string dir = TempDir();
        try
        {
            // WAD A: patches PATCHA, PATCHB; textures TEX_A (uses PATCHA), TEX_AB (uses PATCHA + PATCHB)
            string wadA = Path.Combine(dir, "a.wad");
            var a = new WadBuilder();
            a.AddLump("PNAMES", MakePnames("PATCHA", "PATCHB"));
            a.AddLump("TEXTURE1", MakeTexture1(
                MakeTexture("TEX_A", 64, 64, (0, 0, 0)),
                MakeTexture("TEX_AB", 64, 128, (0, 0, 0), (1, 32, 32))));
            a.AddLump("PATCHA", new byte[8]);
            a.AddLump("PATCHB", new byte[8]);
            a.Write(wadA);

            // WAD B: shares PATCHA by name; texture TEX_B uses it at index 0.
            string wadB = Path.Combine(dir, "b.wad");
            var b = new WadBuilder();
            b.AddLump("PNAMES", MakePnames("PATCHA"));
            b.AddLump("TEXTURE1", MakeTexture1(MakeTexture("TEX_B", 32, 32, (0, 0, 0))));
            b.Write(wadB);

            using var fa = WadFile.Open(wadA);
            using var fb = WadFile.Open(wadB);

            var merger = new TextureMerger();
            merger.MergeSource(fa);
            merger.MergeSource(fb);

            Assert.Equal(new[] { "PATCHA", "PATCHB" }, merger.PatchNames);
            Assert.Equal(3, merger.Textures.Count);

            // TEX_AB should reference PATCHA (0) and PATCHB (1).
            var texAb = merger.Textures.Single(t => t.Name == "TEX_AB");
            Assert.Equal(new[] { 0, 1 }, texAb.Patches.Select(p => p.PatchIndex));

            // TEX_B should remap its PATCHA from local index 0 -> global index 0.
            var texB = merger.Textures.Single(t => t.Name == "TEX_B");
            Assert.Equal(new[] { 0 }, texB.Patches.Select(p => p.PatchIndex));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Texture1_RoundTrips()
    {
        var defs = new[]
        {
            MakeTexture("TEX1", 64, 128, (0, 3, 4), (1, 5, 6)),
            MakeTexture("TEX2", 32, 32, (2, 0, 0)),
        };

        byte[] data = MakeTexture1(defs);
        var parsed = TextureSet.Read(data);

        Assert.Equal(2, parsed.Textures.Count);
        Assert.Equal("TEX1", parsed.Textures[0].Name);
        Assert.Equal((short)64, parsed.Textures[0].Width);
        Assert.Equal((short)128, parsed.Textures[0].Height);
        Assert.Equal(2, parsed.Textures[0].Patches.Count);
        Assert.Equal(new PatchRef(3, 4, 0), parsed.Textures[0].Patches[0]);
        Assert.Equal(new PatchRef(5, 6, 1), parsed.Textures[0].Patches[1]);
        Assert.Equal("TEX2", parsed.Textures[1].Name);
    }

    [Fact]
    public void FullPipeline_RenamesMapsAndMergesTextures()
    {
        string dir = TempDir();
        try
        {
            string wadA = WriteSimpleMapWad(dir, "a.wad", "MAP01", "PATCHA", "TEX1");
            string wadB = WriteSimpleMapWad(dir, "b.wad", "MAP02", "PATCHB", "TEX2");

            string output = Path.Combine(dir, "out.wad");
            var request = new MergeRequest
            {
                InputWadPaths = new[] { wadA, wadB },
                OutputPath = output,
                MapAssignments = new[]
                {
                    new MapAssignment(wadA, "MAP01", "MAP05"),
                    new MapAssignment(wadB, "MAP02", "MAP07"),
                },
                Options = new MergeOptions { AutoAssignMaps = false },
            };

            var result = new WadMerger().Merge(request);

            Assert.True(result.Success, result.Errors.FirstOrDefault());
            Assert.Equal(2, result.MapsAdded);

            using var outWad = WadFile.Open(output);

            // Both maps present under their final slots, with full bodies.
            var mapNames = outWad.Lumps.Where(l => l.Name == "MAP05" || l.Name == "MAP07").ToList();
            Assert.Equal(2, mapNames.Count);

            // Merged textures + both patches present.
            Assert.NotNull(outWad.FindFirst("TEXTURE1"));
            Assert.NotNull(outWad.FindFirst("PNAMES"));
            var texts = TextureSet.Read(outWad.FindFirst("TEXTURE1")!.ReadAll());
            Assert.Equal(2, texts.Textures.Count);
            Assert.Equal("TEX1", texts.Textures[0].Name);
            Assert.Equal("TEX2", texts.Textures[1].Name);

            var pnames = PnamesList.Read(outWad.FindFirst("PNAMES")!.ReadAll());
            Assert.Equal(new[] { "PATCHA", "PATCHB" }, pnames.Names);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string WriteSimpleMapWad(string dir, string fileName, string mapName, string patchName, string textureName)
    {
        string path = Path.Combine(dir, fileName);
        var builder = new WadBuilder();
        builder.AddLump("PNAMES", MakePnames(patchName));
        builder.AddLump("TEXTURE1", MakeTexture1(MakeTexture(textureName, 64, 64, (0, 0, 0))));
        builder.AddLump(patchName, new byte[8]);
        builder.AddLump(mapName, new byte[1]);
        foreach (string body in new[] { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP" })
            builder.AddLump(body, new byte[2]);
        builder.Write(path);
        return path;
    }

    private static string TempDir()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"cwc-{Guid.NewGuid():N}")).FullName;
}