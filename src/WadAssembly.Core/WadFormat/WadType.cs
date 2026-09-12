namespace WadAssembly.Core.WadFormat;

/// <summary>
/// Type of a WAD file, stored in the first 4 bytes of the header.
/// </summary>
public enum WadType
{
    /// <summary>"IWAD" - internal WAD, the game data (DOOM.WAD / DOOM2.WAD).</summary>
    IWad,

    /// <summary>"PWAD" - patch/add-on WAD.</summary>
    PWad,
}