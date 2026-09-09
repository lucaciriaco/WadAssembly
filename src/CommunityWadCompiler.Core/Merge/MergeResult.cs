namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// Outcome of a <see cref="WadMerger"/> run: results and counters the UI renders
/// as a localized report.
/// </summary>
public sealed class MergeResult
{
    public bool Success { get; set; }

    public string? OutputPath { get; set; }

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Info { get; } = new();

    public int MapsAdded { get; set; }

    public int LumpsCopied { get; set; }

    public int DuplicatesSkipped { get; set; }

    public int PatchesMerged { get; set; }

    public int TexturesMerged { get; set; }

    public int TexturesDuplicated { get; set; }

    public int TexturesExcluded { get; set; }

    public int FlatsCopied { get; set; }

    public int MusicCopied { get; set; }

    public long OutputBytes { get; set; }

    public TimeSpan Elapsed { get; set; }
}