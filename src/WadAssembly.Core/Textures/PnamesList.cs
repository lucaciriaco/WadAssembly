namespace WadAssembly.Core.Textures;

/// <summary>
/// PNAMES lump: the global patch-name table referenced by index from TEXTUREx definitions.
/// Binary layout: u32 count, then <c>count</c> 8-byte patch names.
/// </summary>
public sealed class PnamesList
{
    private const int NameLength = 8;

    public List<string> Names { get; } = new();

    public static PnamesList Read(byte[] data)
    {
        var result = new PnamesList();
        if (data.Length < 4)
            return result;

        int count = BitConverter.ToInt32(data, 0);
        count = Math.Clamp(count, 0, (data.Length - 4) / NameLength);

        for (int i = 0; i < count; i++)
        {
            int offset = 4 + i * NameLength;
            string name = WadFormat.WadFile.ReadLumpName(data, offset);
            if (!string.IsNullOrEmpty(name))
                result.Names.Add(name);
        }

        return result;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(Names.Count);
        foreach (string name in Names)
            writer.Write(WadFormat.WadNames.ToWadField(name));
        writer.Flush();
        return ms.ToArray();
    }
}