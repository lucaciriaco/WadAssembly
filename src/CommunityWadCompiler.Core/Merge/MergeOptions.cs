namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// Options that steer how <see cref="WadMerger"/> combines the input WADs.
/// </summary>
public sealed class MergeOptions
{
    /// <summary>
    /// When <c>true</c> (default) maps without an explicit assignment get sequential
    /// slots (MAP01, MAP02, ...) based on the order of inputs and maps.
    /// </summary>
    public bool AutoAssignMaps { get; set; } = true;

    /// <summary>
    /// When <c>true</c> and a base WAD (usually an IWAD) is provided, its texture set
    /// is used as the seed for the merged TEXTURE1/PNAMES.
    /// </summary>
    public bool IncludeBaseWadTextures { get; set; } = true;

    /// <summary>
    /// When <c>true</c> and a base WAD is provided, lumps already present in the base
    /// (e.g. IWAD patches/flats) are not copied again into the output.
    /// </summary>
    public bool SkipBaseWadResources { get; set; } = true;

    /// <summary>Output WAD type. Community builds are PWADs.</summary>
    public WadFormat.WadType OutputType { get; set; } = WadFormat.WadType.PWad;

    /// <summary>
    /// When <c>true</c> and resource WADs are provided, only the textures and flats
    /// actually referenced by the merged maps are implemented in the output.
    /// </summary>
    public bool FilterToUsedResources { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), palette lumps (PLAYPAL, COLORMAP, etc.) from
    /// resource WADs are copied to the output even when filtering to used resources.
    /// </summary>
    public bool IncludePaletteLumps { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, all sprite lumps (S_START..S_END, SS_START..SS_END) from
    /// resource WADs are copied to the output. This is independent of the
    /// <see cref="FilterToUsedResources"/> setting.
    /// </summary>
    public bool IncludeSpriteLumps { get; set; } = false;

    /// <summary>
    /// When <c>true</c> (default), a MAPINFO lump (ZDoom new format) with the level
    /// names of every assigned map is generated. Existing MAPINFO/ZMAPINFO lumps in the
    /// input/resource WADs are then skipped with a warning.
    /// </summary>
    public bool GenerateMapInfo { get; set; } = true;
}