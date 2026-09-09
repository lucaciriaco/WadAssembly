namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// Maps one detected map to its final slot in the output WAD.
/// </summary>
public sealed record MapAssignment(
    string WadPath,
    string OriginalMapName,
    string FinalMapName,
    string? LevelName = null,
    string? MusicName = null,
    string? SkyName = null,
    string? Author = null,
    string? Status = null,
    string? LastModified = null,
    string? Sky2Name = null,
    double SkyScroll = 0,
    double Sky2Scroll = 0);