namespace WadAssembly.Core.Textures;

/// <summary>
/// One patch placement inside a texture definition.
/// </summary>
public sealed record PatchRef(int OriginX, int OriginY, int PatchIndex)
{
    public const int FieldSizeDoom = 10;
    public const int FieldSizeStrife = 6;
}

/// <summary>
/// A parsed wall texture definition (maptexture_t).
/// </summary>
public sealed class TextureDef
{
    public required string Name { get; init; }

    public int Masked { get; init; }

    public short Width { get; init; }

    public short Height { get; init; }

    /// <summary>Obsolete 32-bit field in the on-disk header; preserved as 0.</summary>
    public int ColumnDirectory { get; init; }

    public List<PatchRef> Patches { get; init; } = new();
}