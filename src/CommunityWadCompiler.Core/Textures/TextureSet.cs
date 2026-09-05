namespace CommunityWadCompiler.Core.Textures;

/// <summary>
/// Serializes and deserializes a TEXTURE1/TEXTURE2 lump in the classic Doom format.
///
/// Binary layout:
///   u32 numtextures
///   numtextures × u32 offset (relative to the start of the lump)
///   texture body per offset:
///     name[8], u32 masked, u16 width, u16 height, u32 columndirectory (obsolete),
///     u16 patchcount, then patchcount × { i16 originx, i16 originy, u16 patch, u16 stepdir, u16 colormap }
/// </summary>
public sealed class TextureSet
{
    private const int HeaderDoom = 22;  // name+masked+width+height+columndir+patchcount
    private const int HeaderStrife = 18; // no columndirectory

    public List<TextureDef> Textures { get; } = new();

    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Parses a TEXTUREx lump. Malformed entries are skipped and recorded in <see cref="Warnings"/>.
    /// </summary>
    public static TextureSet Read(byte[] data)
    {
        var result = new TextureSet();
        if (data.Length < 4)
        {
            result.Warnings.Add("Lump vacío o demasiado corto para ser TEXTUREx.");
            return result;
        }

        int count = BitConverter.ToInt32(data, 0);
        if (count < 0 || 4L + count * 4L > data.Length)
        {
            result.Warnings.Add($"Número de texturas inválido ({count}).");
            return result;
        }

        for (int i = 0; i < count; i++)
        {
            int offset = BitConverter.ToInt32(data, 4 + i * 4);
            TextureDef? def = TryParseBody(data, offset, count);
            if (def is not null)
                result.Textures.Add(def);
            else
                result.Warnings.Add($"Textura #{i} (offset {offset}) mal formada; se omite.");
        }

        return result;
    }

    private static TextureDef? TryParseBody(byte[] data, int offset, int fallbackCount)
    {
        bool ValidDoomLayout(out int masked, out short width, out short height, out int columnDirectory, out int patchCount)
        {
            // name[8] + masked(4) + width(2) + height(2)
            if (offset < 0 || offset + HeaderDoom > data.Length)
            {
                masked = 0; width = 0; height = 0; columnDirectory = 0; patchCount = 0;
                return false;
            }
            masked = BitConverter.ToInt32(data, offset + 8);
            width = BitConverter.ToInt16(data, offset + 12);
            height = BitConverter.ToInt16(data, offset + 14);
            columnDirectory = BitConverter.ToInt32(data, offset + 16);
            patchCount = BitConverter.ToInt16(data, offset + 20);
            return width > 0 && width <= 8192 && height > 0 && height <= 8192 &&
                   patchCount >= 0 && patchCount <= 1024;
        }

        bool ValidStrifeLayout(out int patchCount)
        {
            if (offset < 0 || offset + HeaderStrife > data.Length)
            {
                patchCount = 0;
                return false;
            }
            patchCount = BitConverter.ToInt16(data, offset + 16);
            return patchCount >= 0 && patchCount <= 1024;
        }

        string name = offset >= 0 && offset + 8 <= data.Length
            ? WadFormat.WadFile.ReadLumpName(data, offset)
            : $"TEX{fallbackCount}";

        int masked, columnDirectory;
        short width, height;
        int count;
        int stride;

        if (ValidDoomLayout(out masked, out width, out height, out columnDirectory, out count))
        {
            stride = PatchRef.FieldSizeDoom;
        }
        else if (ValidStrifeLayout(out count))
        {
            masked = 0; width = BitConverter.ToInt16(data, offset + 8);
            height = BitConverter.ToInt16(data, offset + 10); columnDirectory = 0;
            stride = PatchRef.FieldSizeStrife;
        }
        else
        {
            return null;
        }

        int headerSize = stride == PatchRef.FieldSizeDoom ? HeaderDoom : HeaderStrife;
        long end = (long)offset + headerSize + count * stride;
        if (end > data.Length)
            return null;

        var def = new TextureDef { Name = name, Masked = masked, Width = width, Height = height, ColumnDirectory = columnDirectory };

        for (int p = 0; p < count; p++)
        {
            int patchOffset = offset + headerSize + p * stride;
            int originX = BitConverter.ToInt16(data, patchOffset);
            int originY = BitConverter.ToInt16(data, patchOffset + 2);
            int patchIndex = BitConverter.ToInt16(data, patchOffset + 4);
            def.Patches.Add(new PatchRef(originX, originY, patchIndex));
        }

        return def;
    }

    /// <summary>Serializes the texture set in the classic Doom format.</summary>
    public byte[] Write()
    {
        using var ms = new MemoryStream();
        var offsets = new int[Textures.Count];

        ms.Write(BitConverter.GetBytes(Textures.Count), 0, 4);
        for (int i = 0; i < Textures.Count; i++)
            ms.Write(new byte[4], 0, 4); // offset placeholders, patched below

        for (int i = 0; i < Textures.Count; i++)
        {
            ms.Position = Align4(ms.Position);
            offsets[i] = checked((int)ms.Position);
            WriteBody(ms, Textures[i]);
        }

        for (int i = 0; i < Textures.Count; i++)
        {
            ms.Position = 4 + i * 4L;
            ms.Write(BitConverter.GetBytes(offsets[i]), 0, 4);
        }

        return ms.ToArray();
    }

    private static void WriteBody(Stream ms, TextureDef def)
    {
        byte[] value = WadFormat.WadNames.ToWadField(def.Name);
        ms.Write(value, 0, value.Length);
        value = BitConverter.GetBytes(def.Masked);
        ms.Write(value, 0, value.Length);
        value = BitConverter.GetBytes(def.Width);
        ms.Write(value, 0, value.Length);
        value = BitConverter.GetBytes(def.Height);
        ms.Write(value, 0, value.Length);
        value = BitConverter.GetBytes(0); // columndirectory (obsolete)
        ms.Write(value, 0, value.Length);
        value = BitConverter.GetBytes((short)def.Patches.Count);
        ms.Write(value, 0, value.Length);

        foreach (var patch in def.Patches)
        {
            value = BitConverter.GetBytes((short)patch.OriginX);
            ms.Write(value, 0, value.Length);
            value = BitConverter.GetBytes((short)patch.OriginY);
            ms.Write(value, 0, value.Length);
            value = BitConverter.GetBytes((short)patch.PatchIndex);
            ms.Write(value, 0, value.Length);
            value = BitConverter.GetBytes((short)0); // stepdir
            ms.Write(value, 0, value.Length);
            value = BitConverter.GetBytes((short)0); // colormap
            ms.Write(value, 0, value.Length);
        }
    }

    private static long Align4(long value) => (value + 3) & ~3L;
}