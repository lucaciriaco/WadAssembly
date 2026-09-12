namespace WadAssembly.Core.Merge;

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

    /// <summary>
    /// When <c>true</c> and the "filter to used resources" mode is active, a wall texture
    /// or flat actually used by a map is expanded to its whole numbered run: if the pack
    /// defines an animated texture as consecutive numbered names (e.g. <c>COMPSTA1</c> ..
    /// <c>COMPSTA6</c>) without an ANIMATED/ANIMDEFS lump, every frame of the run is kept
    /// in the merged WAD so the animation plays. Only the run anchored on a used name is
    /// expanded (never every numbered texture), and only if the run does not exceed
    /// <see cref="MaxAnimatedTextureFrames"/> (to avoid importing big caches of variant
    /// textures that merely share a numeric suffix, e.g. <c>SHAWN1</c>..<c>SHAWN24</c>).
    /// </summary>
    public bool ExpandAnimatedTextureRuns { get; set; } = false;

    /// <summary>Maximum number of frames a numbered texture run may have to be treated as
    /// an animation by <see cref="ExpandAnimatedTextureRuns"/>. Runs longer than this cap
    /// are left untouched (they are usually variant sets, not animation frames).</summary>
    public int MaxAnimatedTextureFrames { get; set; } = 8;

    /// <summary>
    /// When <c>true</c>, a text <c>ANIMDEFS</c> lump is generated in the output WAD from the
    /// numbered runs expanded by <see cref="ExpandAnimatedTextureRuns"/> (see also
    /// <see cref="MaxAnimatedTextureFrames"/>), so engines actually animate the frames
    /// (ZDoom-family ports that read ANIMDEFS). Enabling this option also implies importing
    /// the whole runs, even if <see cref="ExpandAnimatedTextureRuns"/> is <c>false</c>: an
    /// ANIMDEFS entry pointing at frames that were never imported would break the animation.
    /// One entry per run is emitted: <c>animatedTexture &lt;tics&gt;, { A, B, ... }</c> for wall
    /// runs and <c>animatedFloor &lt;tics&gt;, { A, B, ... }</c> for flat runs, in numeric order.
    /// Runs longer than <see cref="MaxAnimatedTextureFrames"/> are never declared (they are
    /// variants, not animations). Only runs whose prefix appears in
    /// <see cref="AnimatedPrefixes"/> are declared (variant sets such as <c>SKY1</c>/<c>SKY2</c>
    /// stay static). If a resource WAD already provides an ANIMDEFS lump, the
    /// generated entries are appended to its text instead of replacing it.
    /// </summary>
    public bool GenerateAnimdefsForAnimatedRuns { get; set; } = false;

    /// <summary>Tics per frame used by the generated ANIMDEFS entries (ZDoom: 35 tics = 1
    /// second). Only meaningful when <see cref="GenerateAnimdefsForAnimatedRuns"/> is on.</summary>
    public int AnimatedRunTics { get; set; } = 8;

    /// <summary>
    /// Comma, semicolon, space or newline-separated list of numeric-run prefixes declared as
    /// animations by <see cref="GenerateAnimdefsForAnimatedRuns"/> and
    /// <see cref="GenerateAnimatedLumpForRuns"/>. Only runs whose prefix appears in this list
    /// are turned into animation entries; runs of any other prefix are still imported whole
    /// when a map uses one of their frames (variant sets such as <c>SKY1</c>/<c>SKY2</c> or
    /// <c>BIGDOOR1</c>..<c>BIGDOOR7</c> stay static — they are not animated). Matching is
    /// case-insensitive, on the part of the name before the trailing number. Empty by default:
    /// no animation is declared until prefixes are configured.
    /// </summary>
    public string AnimatedPrefixes { get; set; } = "";

    /// <summary>
    /// When <c>true</c>, a binary <c>ANIMATED</c> lump (Boom/MBF format, understood by the
    /// vanilla renderers of Boom-based ports such as Odamex) is generated in the output WAD
    /// from the numbered runs expanded by <see cref="ExpandAnimatedTextureRuns"/> (see also
    /// <see cref="MaxAnimatedTextureFrames"/>). Enabling this option also implies importing
    /// the whole runs, even if <see cref="ExpandAnimatedTextureRuns"/> is <c>false</c>.
    /// One 23-byte entry per run is written (<c>istexture=1</c> for wall textures,
    /// <c>istexture=0</c> for flats; first frame as <c>startname</c>, last frame as
    /// <c>endname</c>; <see cref="AnimatedRunTics"/> as speed) and the lump ends with the
    /// standard 0xFF byte. Runs longer than <see cref="MaxAnimatedTextureFrames"/> are never
    /// declared. Only runs whose prefix appears in <see cref="AnimatedPrefixes"/> are
    /// declared (variant sets such as <c>SKY1</c>/<c>SKY2</c> stay static). If a resource WAD
    /// already provides an ANIMATED lump, the generated entries are
    /// appended to it (its terminator byte is kept last) instead of replacing it.
    /// </summary>
    public bool GenerateAnimatedLumpForRuns { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, a binary <c>SWITCHES</c> lump (Boom/MBF format) is generated in the
    /// output WAD from the switch pairs (<c>SW1xxx</c>/<c>SW2xxx</c>) present in the merged
    /// textures, so Boom-based ports (such as Odamex) recognize the pack's switches. Enabling
    /// this option also imports the partner frame of any used switch (a map using <c>SW1BRN</c>
    /// has <c>SW2BRN</c> kept too). Entries are 20 bytes each: off texture (<c>SW1...</c>), on
    /// texture (<c>SW2...</c>) and an int16 flag of 0. If a resource WAD already provides a
    /// SWITCHES lump, the generated entries are appended to it.
    /// </summary>
    public bool GenerateSwitchesLumpForPairs { get; set; } = false;

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