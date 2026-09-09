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

    /// <summary>Lump name of the music played during the intermission screens
    /// between levels; referenced by the generated MAPINFO.</summary>
    public string? IntermissionMusic { get; set; }

    /// <summary>Absolute path of an external music file for the intermission.</summary>
    public string? IntermissionMusicExternalPath { get; set; }
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

    /// <summary>Absolute path of an external music file (.mid/.mod/.it) for this slot.</summary>
    public string? MusicExternalPath { get; set; }

    /// <summary>Author of the map, written as a comment in the generated MAPINFO.</summary>
    public string? Author { get; set; }

    /// <summary>Progress status of the map (TODO/WIP/DONE/FIX), written as a comment in the MAPINFO.</summary>
    public string? Status { get; set; }

    /// <summary>Additional free-text notes for this map (project metadata only).</summary>
    public string? Notes { get; set; }

    public string? SkyName { get; set; }

    /// <summary>Optional second sky layer name (sky2 in MAPINFO); empty when disabled.</summary>
    public string? Sky2Name { get; set; }

    /// <summary>Horizontal rotation speed of the sky (MAPINFO sky1/sky2 offset); 0 = static.</summary>
    public double SkyScroll { get; set; }

    /// <summary>Horizontal rotation speed of the sky2 layer; 0 = static.</summary>
    public double Sky2Scroll { get; set; }

    /// <summary>True when the second sky layer (Sky2) is enabled for this map.</summary>
    public bool EnableSky2 { get; set; }
}