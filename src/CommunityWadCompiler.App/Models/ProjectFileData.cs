namespace CommunityWadCompiler.App.Models;

/// <summary>Serializable project definition (save/load via JSON).</summary>
public sealed class ProjectFileData
{
    public int Version { get; set; } = 1;

    public string? BaseWadPath { get; set; }

    public string? OutputPath { get; set; }

    public List<string> WadPaths { get; set; } = new();

    public List<string> ResourceWadPaths { get; set; } = new();

    public List<MapEntryData> Maps { get; set; } = new();

    public bool AutoAssignMaps { get; set; } = true;

    public bool FilterResourcesToUsed { get; set; } = true;
}

public sealed class MapEntryData
{
    public string WadPath { get; set; } = "";

    public string OriginalName { get; set; } = "";

    public string FinalName { get; set; } = "";

    public bool IsUdmf { get; set; }

    /// <summary>Readable level name used to build the generated MAPINFO lump.</summary>
    public string? LevelName { get; set; }
}