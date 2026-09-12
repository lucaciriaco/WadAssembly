using WadAssembly.Core.Maps;
using WadAssembly.Core.Merge;
using Xunit;
using WadAssembly.Core.Textures;
using WadAssembly.Core.WadFormat;

namespace WadAssembly.Tests;

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

    [Fact]
    public void BaseWadTextures_AreSeededButResourceWadOverridesSameName()
    {
        string dir = TempDir();
        try
        {
            // Base "IWAD-like" WAD: textures TEX1 (64x64) and TEX2 (base-only), plus a flat FLAT01.
            string baseWad = Path.Combine(dir, "base.wad");
            var b = new WadBuilder();
            b.AddLump("PNAMES", MakePnames("BASEPAT"));
            b.AddLump("TEXTURE1", MakeTexture1(
                MakeTexture("TEX1", 64, 64, (0, 0, 0)),
                MakeTexture("TEX2", 64, 64, (0, 0, 0))));
            b.AddLump("BASEPAT", new byte[8]);
            b.AddLump("FLAT01", new byte[8]);
            b.Write(baseWad);

            // Resource WAD: intentionally redefines TEX1 (128x256) with its own patch,
            // and replaces the base flat FLAT01 (same name, different content).
            string resources = Path.Combine(dir, "resources.wad");
            var r = new WadBuilder();
            r.AddLump("PNAMES", MakePnames("RESPATCH"));
            r.AddLump("TEXTURE1", MakeTexture1(MakeTexture("TEX1", 128, 256, (0, 0, 0))));
            r.AddLump("RESPATCH", new byte[8]);
            r.AddLump("F_START", new byte[0]);
            r.AddLump("FLAT01", new byte[8]);
            r.AddLump("F_END", new byte[0]);
            r.Write(resources);

            string map = WriteSimpleMapWad(dir, "map.wad", "MAP01", "IGNORED", "IGNORED");
            string output = Path.Combine(dir, "out.wad");
            var request = new MergeRequest
            {
                BaseWadPath = baseWad,
                ResourceWadPaths = new[] { resources },
                InputWadPaths = new[] { map },
                OutputPath = output,
                Options = new MergeOptions { FilterToUsedResources = false, BaseTexturesOverrideResources = false },
            };

            var result = new WadMerger().Merge(request);

            Assert.True(result.Success, result.Errors.FirstOrDefault());
            using var outWad = WadFile.Open(output);

            // The resource WAD's TEX1 (replacement) is the one compiled in, not the base's.
            var textures = TextureSet.Read(outWad.FindFirst("TEXTURE1")!.ReadAll());
            var tex1 = textures.Textures.Single(t => t.Name == "TEX1");
            Assert.Equal((short)128, tex1.Width);
            Assert.Equal((short)256, tex1.Height);
            Assert.Equal(new[] { 0 }, tex1.Patches.Select(p => p.PatchIndex));

            // Base-only textures are still seeded as a fallback (TEX2 references BASEPAT).
            var tex2 = textures.Textures.Single(t => t.Name == "TEX2");
            Assert.Equal(new[] { 1 }, tex2.Patches.Select(p => p.PatchIndex));

            // PNAMES: sources first (RESPATCH=0), base as fallback (BASEPAT=1).
            var pnames = PnamesList.Read(outWad.FindFirst("PNAMES")!.ReadAll());
            Assert.Equal(new[] { "RESPATCH", "BASEPAT" }, pnames.Names);

            // Intentional replacement must not be reported as a duplicate warning.
            Assert.DoesNotContain(result.Warnings, w => w.Contains("TEX1"));

            // Resource graphics with base-same names are compiled in (patch + replaced flat).
            Assert.NotNull(outWad.FindFirst("RESPATCH"));
            Assert.NotNull(outWad.FindFirst("FLAT01"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DefaultBehavior_BaseTexturesWin_AndResourceDuplicatesAreSkipped()
    {
        string dir = TempDir();
        try
        {
            string baseWad = Path.Combine(dir, "base.wad");
            var b = new WadBuilder();
            b.AddLump("PNAMES", MakePnames("BASEPAT"));
            b.AddLump("TEXTURE1", MakeTexture1(
                MakeTexture("TEX1", 64, 64, (0, 0, 0)),
                MakeTexture("TEX2", 64, 64, (0, 0, 0))));
            b.AddLump("BASEPAT", new byte[8]);
            b.AddLump("FLAT01", new byte[8]);
            b.Write(baseWad);

            // Same resource pack redefining TEX1 and providing a base-named flat.
            string resources = Path.Combine(dir, "resources.wad");
            var r = new WadBuilder();
            r.AddLump("PNAMES", MakePnames("RESPATCH"));
            r.AddLump("TEXTURE1", MakeTexture1(MakeTexture("TEX1", 128, 256, (0, 0, 0))));
            r.AddLump("RESPATCH", new byte[8]);
            r.AddLump("F_START", new byte[0]);
            r.AddLump("FLAT01", new byte[8]);
            r.AddLump("F_END", new byte[0]);
            r.Write(resources);

            string map = WriteSimpleMapWad(dir, "map.wad", "MAP01", "IGNORED", "IGNORED");
            string output = Path.Combine(dir, "out.wad");
            var request = new MergeRequest
            {
                BaseWadPath = baseWad,
                ResourceWadPaths = new[] { resources },
                InputWadPaths = new[] { map },
                OutputPath = output,
                // Default options: BaseTexturesOverrideResources = true.
                Options = new MergeOptions { FilterToUsedResources = false },
            };

            var result = new WadMerger().Merge(request);

            Assert.True(result.Success, result.Errors.FirstOrDefault());
            using var outWad = WadFile.Open(output);

            // The base IWAD's TEX1 wins (64x64, BASEPAT at global 0).
            var textures = TextureSet.Read(outWad.FindFirst("TEXTURE1")!.ReadAll());
            var tex1 = textures.Textures.Single(t => t.Name == "TEX1");
            Assert.Equal((short)64, tex1.Width);
            Assert.Equal((short)64, tex1.Height);
            Assert.Equal(new[] { 0 }, tex1.Patches.Select(p => p.PatchIndex));

            // PNAMES: base seed first (BASEPAT=0); the resource TEX1 was skipped, but its
            // patch names still seed PNAMES (unused RESPPATCH stays as a harmless entry).
            var pnames = PnamesList.Read(outWad.FindFirst("PNAMES")!.ReadAll());
            Assert.Equal(new[] { "BASEPAT", "RESPATCH" }, pnames.Names);

            // The resource's same-named TEX1 is reported as a duplicate (not silent).
            Assert.Contains(result.Warnings, w => w.Contains("TEX1"));

            // Base-named resource lumps (FLAT01) are NOT compiled in by default; a new patch
            // name (RESPATCH) still is (it is not a base-skip candidate).
            Assert.NotNull(outWad.FindFirst("RESPATCH"));
            Assert.Null(outWad.FindFirst("FLAT01"));
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