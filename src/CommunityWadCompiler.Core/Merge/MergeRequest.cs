namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// Named inputs for a single compile run.
/// </summary>
public sealed class MergeRequest
{
    /// <summary>
    /// Optional base WAD (typically DOOM.WAD / DOOM2.WAD) whose texture definitions
    /// seed the merged texture set and whose resources are not re-copied.
    /// </summary>
    public string? BaseWadPath { get; init; }

    /// <summary>Add-on WADs contributed by community members, in merge order.</summary>
    public IReadOnlyList<string> InputWadPaths { get; init; } = Array.Empty<string>();

    /// <summary>Output WAD path.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Explicit map slot assignments (empty means assign automatically).</summary>
    public IReadOnlyList<MapAssignment> MapAssignments { get; init; } = Array.Empty<MapAssignment>();

    public MergeOptions Options { get; init; } = new();
}