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

    /// <summary>Writes the human-readable notes/comments block into the generated MAPINFO.</summary>
    public bool IncludeMapInfoNotes { get; set; } = true;

    /// <summary>When true (default) the base IWAD textures win over same-named textures from
    /// resource WADs; when false, resource textures override the base IWAD's. Old projects
    /// without the field default to true (the engine-friendly behavior).</summary>
    public bool BaseTexturesOverrideResources { get; set; } = true;

    /// <summary>When true and filtering to used resources, animated textures numbered
    /// consecutively (e.g. COMPSTA1..COMPSTA6) are imported whole when the map uses one frame.</summary>
    public bool ExpandAnimatedTextureRuns { get; set; } = false;

    /// <summary>Maximum frames a numbered texture run may have to be treated as an animation.</summary>
    public int MaxAnimatedTextureFrames { get; set; } = 8;

    /// <summary>When true, a text ANIMDEFS lump declaring the numbered animated runs used by
    /// the maps is generated in the output (implies importing the whole runs).</summary>
    public bool GenerateAnimdefsForAnimatedRuns { get; set; } = false;

    /// <summary>Tics per frame for the generated ANIMDEFS entries (35 tics = 1 second).</summary>
    public int AnimatedRunTics { get; set; } = 8;

    /// <summary>When true, a binary ANIMATED lump (Boom/MBF format, read by the vanilla
    /// renderer of Boom-based ports such as Odamex) declaring the numbered animated runs
    /// used by the maps is generated in the output (implies importing the whole runs).</summary>
    public bool GenerateAnimatedLumpForRuns { get; set; } = false;

    /// <summary>When true, a binary SWITCHES lump (Boom/MBF format, read by Boom-based
    /// ports such as Odamex) is generated from the SW1xxx/SW2xxx switch pairs present in
    /// the merged textures; the partner frame of any used switch is kept too.</summary>
    public bool GenerateSwitchesLumpForPairs { get; set; } = false;

    /// <summary>Comma/semicolon/newline-separated list of numeric-run prefixes declared as
    /// animations by the generated ANIMATED/ANIMDEFS lumps (only those prefixes animate;
    /// variant sets like SKY1/SKY2 stay static).</summary>
    public string? AnimatedPrefixes { get; set; } = "";

    /// <summary>Target engine chosen in Project Settings: "ZDoom" shows the ANIMDEFS
    /// generation option, "Boom" shows the SWITCHES/ANIMATED (Boom/MBF) options.
    /// Old projects without the field default to "Boom".</summary>
    public string? TargetEngine { get; set; } = "Boom";

    /// <summary>When true, the plan sheet tints the status cell (TODO/WIP/DONE/FIX)
    /// with the matching status color.</summary>
    public bool ColorByStatus { get; set; }

    /// <summary>Lump name of the music played during the intermission screens
    /// between levels; referenced by the generated MAPINFO.</summary>
    public string? IntermissionMusic { get; set; }

    /// <summary>Absolute path of an external music file for the intermission.</summary>
    public string? IntermissionMusicExternalPath { get; set; }

    /// <summary>Executable path of the source port used by "Compile and run";
    /// null when the project is only compiled (not run).</summary>
    public string? SourcePortPath { get; set; }
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