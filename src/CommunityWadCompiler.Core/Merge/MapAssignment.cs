namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// Maps one detected map to its final slot in the output WAD.
/// </summary>
public sealed record MapAssignment(
    string WadPath,
    string OriginalMapName,
    string FinalMapName,
    string? LevelName = null);