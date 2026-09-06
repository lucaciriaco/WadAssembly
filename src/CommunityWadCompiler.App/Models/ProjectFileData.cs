namespace CommunityWadCompiler.App.Models;

/// <summary>Serializable project definition (save/load via JSON).</summary>
public sealed class ProjectFileData
{
    public int Version { get; set; } = 1;

    /// <summary>Name of the project, stored as a comment in the generated MAPINFO.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Editable version prefix (e.g. "1.2.3"), stored as a comment (prefixed to the
    /// automatic compile timestamp).</summary>
    public string? VersionPrefix { get; set; }

    /// <summary>Total number of slots of the PWAD, shown in the maps/slots sheet.</summary>
    public int MapSlots { get; set; } = 32;

    /// <summary>Project collaborators (map authors), kept as project metadata only.</summary>
    public List<string> Collaborators { get; set; } = new();

    public string? BaseWadPath { get; set; }

    public string? OutputPath { get; set; }

    public List<string> WadPaths { get; set; } = new();

    public List<string> ResourceWadPaths { get; set; } = new();

    public List<MapEntryData> Maps { get; set; } = new();

    public bool AutoAssignMaps { get; set; } = true;

    public bool FilterResourcesToUsed { get; set; } = true;

    public bool IncludePaletteLumps { get; set; } = true;

    public bool IncludeSpriteLumps { get; set; } = false;
}

public sealed class MapEntryData
{
    public string WadPath { get; set; } = "";

    public string OriginalName { get; set; } = "";

    public string FinalName { get; set; } = "";

    public bool IsUdmf { get; set; }

    /// <summary>Readable level name used to build the generated MAPINFO lump.</summary>
    public string? LevelName { get; set; }

    /// <summary>Music lump assigned to the map, referenced by the generated MAPINFO.</summary>
    public string? MusicName { get; set; }

    /// <summary>Author of the map, written as a comment in the generated MAPINFO.</summary>
    public string? Author { get; set; }

    /// <summary>Progress status of the map (TODO/WIP/DONE/FIX), written as a comment in the MAPINFO.</summary>
    public string? Status { get; set; }
}