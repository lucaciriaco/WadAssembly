using System.Text;
using CommunityWadCompiler.Core.Music;
using CommunityWadCompiler.Core.WadFormat;
using Xunit;

namespace CommunityWadCompiler.Tests;

public class MusicLumpDetectorTests
{
    [Theory]
    [InlineData("MUS\x1A")] // MUS
    [InlineData("MThd")]    // MIDI
    [InlineData("IMPM")]    // Impulse Tracker
    public void IsMusicData_StartSignatures_AreDetected(string signature)
    {
        byte[] data = Encoding.ASCII.GetBytes(signature);
        Assert.True(MusicLumpDetector.IsMusicData(data));
    }

    [Theory]
    [InlineData("TEXT")]
    [InlineData("PLAY")]
    [InlineData("SPRY")]
    public void IsMusicData_UnrelatedSignature_IsRejected(string signature)
    {
        byte[] data = Encoding.ASCII.GetBytes(signature);
        Assert.False(MusicLumpDetector.IsMusicData(data));
    }

    [Theory]
    [InlineData("M.K.")]
    [InlineData("M!K!")]
    [InlineData("M&K!")]
    [InlineData("N.T.")]
    [InlineData("OKTA")]
    [InlineData("8CHN")]
    [InlineData("FLT4")]
    [InlineData("16CN")]
    public void IsMusicData_ProTrackerModSignatureAtOffset1080_IsDetected(string signature)
    {
        var mod = new byte[1084];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes(signature), 0, mod, 1080, 4);
        Assert.True(MusicLumpDetector.IsMusicData(mod));
    }

    [Fact]
    public void IsMusicData_ModSignatureMismatch_IsRejected()
    {
        var mod = new byte[1084];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("XXXX"), 0, mod, 1080, 4);
        Assert.False(MusicLumpDetector.IsMusicData(mod));
    }

    [Fact]
    public void IsMusicData_TooShortForModHeader_IsNotMod()
    {
        // 4 bytes alone are enough for an IT header but not for a MOD signature at 1080.
        Assert.False(MusicLumpDetector.IsMusicData(Encoding.ASCII.GetBytes("M.K.")));
    }

    [Fact]
    public void DetectNames_FindsItAndModLumpsInsideWad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cwc-music-{Guid.NewGuid():N}.wad");
        var builder = new WadBuilder();
        builder.AddLump("D_RUNNIN", new byte[] { (byte)'M', (byte)'U', (byte)'S', 0x1A, 0, 0, 0, 0 });
        builder.AddLump("TRACK1", Encoding.ASCII.GetBytes("IMPMxxxxxx"));
        byte[] mod = new byte[1084];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("M.K."), 0, mod, 1080, 4);
        builder.AddLump("SONGMOD", mod);
        builder.Write(path);

        try
        {
            using var wad = WadFile.Open(path);
            IReadOnlyList<string> names = MusicLumpDetector.DetectNames(wad);
            Assert.Contains("D_RUNNIN", names);
            Assert.Contains("TRACK1", names);
            Assert.Contains("SONGMOD", names);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CollectAcrossWads_IncludesItAndModCandidates()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cwc-itmod-{Guid.NewGuid():N}.wad");
        var builder = new WadBuilder();
        builder.AddLump("IMP1", Encoding.ASCII.GetBytes("IMPMxxxxxxmod"));
        byte[] mod = new byte[1084];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("6CHN"), 0, mod, 1080, 4);
        builder.AddLump("MOD1", mod);
        builder.Write(path);

        try
        {
            using var wad = WadFile.Open(path);
            IReadOnlyList<MusicCandidate> candidates = MusicLumpDetector.CollectAcrossWads(new[] { wad });
            Assert.Contains(candidates, c => c.OriginalName == "IMP1");
            Assert.Contains(candidates, c => c.OriginalName == "MOD1");
        }
        finally
        {
            File.Delete(path);
        }
    }
}