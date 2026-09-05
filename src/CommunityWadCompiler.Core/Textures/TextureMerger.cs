using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.Core.Textures;

/// <summary>
/// Merges PNAMES + TEXTURE1 + TEXTURE2 from several WADs into a single patch table
/// and texture set. Patches are deduplicated by name (first occurrence wins); textures
/// are deduplicated by name (first definition wins, matching engine semantics).
/// </summary>
public sealed class TextureMerger
{
    private readonly List<string> _patchNames = new();
    private readonly Dictionary<string, int> _patchIndex = new(StringComparer.Ordinal);
    private readonly List<TextureDef> _textures = new();
    private readonly Dictionary<string, int> _textureIndex = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = new();
    private int _skippedTextures;

    /// <summary>Master list of merged patch names (order equals final PNAMES indices).</summary>
    public IReadOnlyList<string> PatchNames => _patchNames;

    /// <summary>Merged texture definitions.</summary>
    public IReadOnlyList<TextureDef> Textures => _textures;

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Textures skipped because another definition with the same name existed.</summary>
    public int SkippedTextures => _skippedTextures;

    public bool HasContent => _patchNames.Count > 0 || _textures.Count > 0;

    /// <summary>
    /// Merges every PNAMES/TEXTURE1/TEXTURE2 lump of the given WAD, in that engine order.
    /// </summary>
    /// <param name="wad">Source WAD. Wads without any of these lumps are ignored.</param>
    public void MergeSource(WadFile wad)
    {
        Lump? pnamesLump = wad.FindLast("PNAMES");
        if (pnamesLump is null)
        {
            if (wad.FindLast("TEXTURE1") is null && wad.FindLast("TEXTURE2") is null)
                return; // nothing to merge from this WAD
            _warnings.Add($"{wad.SourcePath}: contiene TEXTUREx sin PNAMES; se intenta resolver por nombre.");
        }

        int[] remap = MapPatchTable(pnamesLump);

        MergeTextureLump(wad, "TEXTURE1", remap);
        MergeTextureLump(wad, "TEXTURE2", remap);
    }

    private int[] MapPatchTable(Lump? pnamesLump)
    {
        if (pnamesLump is null)
            return Array.Empty<int>();

        var source = PnamesList.Read(pnamesLump.ReadAll());
        var remap = new int[source.Names.Count];

        for (int i = 0; i < source.Names.Count; i++)
        {
            string name = source.Names[i];
            if (_patchIndex.TryGetValue(name, out int existing))
            {
                remap[i] = existing;
            }
            else
            {
                remap[i] = _patchNames.Count;
                _patchNames.Add(name);
                _patchIndex[name] = remap[i];
            }
        }

        return remap;
    }

    private void MergeTextureLump(WadFile wad, string lumpName, int[] patchRemap)
    {
        Lump? lump = wad.FindLast(lumpName);
        if (lump is null)
            return;

        var set = TextureSet.Read(lump.ReadAll());
        _warnings.AddRange(set.Warnings);

        foreach (var def in set.Textures)
        {
            if (_textureIndex.ContainsKey(def.Name))
            {
                _skippedTextures++;
                _warnings.Add($"Textura '{def.Name}' duplicada; se mantiene la primera definición.");
                continue;
            }

            var entry = new TextureDef
            {
                Name = def.Name,
                Masked = def.Masked,
                Width = def.Width,
                Height = def.Height,
                ColumnDirectory = def.ColumnDirectory,
            };

            foreach (var patch in def.Patches)
            {
                int newIndex;
                if (patchRemap.Length == 0)
                {
                    // No PNAMES in the source: reference the patch by name when known,
                    // otherwise keep the index as-is.
                    newIndex = patch.PatchIndex;
                }
                else if (patch.PatchIndex >= 0 && patch.PatchIndex < patchRemap.Length)
                {
                    newIndex = patchRemap[patch.PatchIndex];
                }
                else
                {
                    _warnings.Add($"Textura '{def.Name}': índice de patch {patch.PatchIndex} fuera de rango.");
                    newIndex = 0;
                }

                entry.Patches.Add(new PatchRef(patch.OriginX, patch.OriginY, newIndex));
            }

            _textureIndex[entry.Name] = _textures.Count;
            _textures.Add(entry);
        }
    }

    /// <summary>
    /// Removes every texture whose name is not in <paramref name="usedTextureNames"/>, then
    /// rebuilds the patch table so only patches referenced by the remaining textures survive.
    /// </summary>
    /// <returns>The number of textures removed.</returns>
    public int ApplyUsageFilter(IReadOnlySet<string> usedTextureNames)
    {
        var kept = new List<TextureDef>(_textures.Count);
        int removed = 0;
        foreach (var def in _textures)
        {
            if (usedTextureNames.Contains(def.Name))
                kept.Add(def);
            else
                removed++;
        }

        var referenced = new bool[_patchNames.Count];
        foreach (var def in kept)
        {
            foreach (var patch in def.Patches)
            {
                if (patch.PatchIndex >= 0 && patch.PatchIndex < referenced.Length)
                    referenced[patch.PatchIndex] = true;
            }
        }

        var newPatchNames = new List<string>();
        var remap = new int[_patchNames.Count];
        for (int i = 0; i < _patchNames.Count; i++)
        {
            if (referenced[i])
            {
                remap[i] = newPatchNames.Count;
                newPatchNames.Add(_patchNames[i]);
            }
            else
            {
                remap[i] = -1;
            }
        }

        // Rewrite patch indices before publishing the new tables.
        for (int i = 0; i < kept.Count; i++)
        {
            var def = kept[i];
            for (int p = 0; p < def.Patches.Count; p++)
            {
                var patch = def.Patches[p];
                if (patch.PatchIndex >= 0 && patch.PatchIndex < remap.Length && remap[patch.PatchIndex] >= 0)
                    def.Patches[p] = new PatchRef(patch.OriginX, patch.OriginY, remap[patch.PatchIndex]);
            }
        }

        _patchNames.Clear();
        _patchNames.AddRange(newPatchNames);
        _patchIndex.Clear();
        for (int i = 0; i < newPatchNames.Count; i++)
            _patchIndex[newPatchNames[i]] = i;

        _textures.Clear();
        _textures.AddRange(kept);
        _textureIndex.Clear();
        for (int i = 0; i < kept.Count; i++)
            _textureIndex[kept[i].Name] = i;

        return removed;
    }

    /// <summary>Builds the final merged PNAMES lump bytes.</summary>
    public byte[] BuildPnames()
    {
        var list = new PnamesList();
        list.Names.AddRange(_patchNames);
        return list.Write();
    }

    /// <summary>Builds the final merged TEXTURE1 lump bytes.</summary>
    public byte[] BuildTexture1()
    {
        var set = new TextureSet();
        set.Textures.AddRange(_textures);
        return set.Write();
    }
}