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
    /// When <c>true</c> (default) the base IWAD textures win over same-named textures from
    /// the resource/input WADs: the base is merged first and the resource duplicates are
    /// reported as warnings. When <c>false</c>, the resource WADs are merged first (their
    /// definitions win, so a pack can intentionally replace an IWAD texture of the same name)
    /// and the base IWAD only fills the names they do not provide, as a silent fallback.
    /// </summary>
    public bool BaseTexturesOverrideResources { get; set; } = true;

    /// <summary>
    /// When <c>true</c> and a base WAD (usually an IWAD) is provided, its texture set is
    /// used as the seed/fallback for the merged TEXTURE1/PNAMES (see
    /// <see cref="BaseTexturesOverrideResources"/> for who wins on same names).
    /// </summary>
    public bool IncludeBaseWadTextures { get; set; } = true;

    /// <summary>
    /// When <c>true</c> and a base WAD is provided, lumps already present in the base
    /// (e.g. IWAD patches/flats) are not copied again into the output. Resource WADs are
    /// exempt when <see cref="BaseTexturesOverrideResources"/> is <c>false</c>: their
    /// same-named lumps are intentional replacements and are compiled in.
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
    /// When <c>true</c>, all sprite lumps (S_START..S_END, SS_START..SS_END),
    /// status-bar graphics (ST_START..ST_END, STRT_START..STRT_END) and fonts
    /// (FM_START..FM_END) from resource WADs are copied to the output. This is
    /// independent of the <see cref="FilterToUsedResources"/> setting.
    /// </summary>
    public bool IncludeSpriteLumps { get; set; } = false;

    /// <summary>
    /// When <c>true</c> (default), a MAPINFO lump (ZDoom new format) with the level
    /// names of every assigned map is generated. Existing MAPINFO/ZMAPINFO lumps in the
    /// input/resource WADs are then skipped with a warning.
    /// </summary>
    public bool GenerateMapInfo { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), the generated MAPINFO carries the human-readable
    /// notes/comments block (generator header, project name, version, compile date and
    /// the per-map author/status/last-modification comments). Disable to emit a clean
    /// MAPINFO lump without any <c>//</c> comment lines.
    /// </summary>
    public bool IncludeMapInfoNotes { get; set; } = true;
}