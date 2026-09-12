using System.Text;

namespace WadAssembly.Core.WadFormat;

/// <summary>
/// Incrementally accumulates lumps and writes them out as a single, well-formed
/// PWAD/IWAD file (data 4-byte aligned, directory last, header patched).
/// </summary>
public sealed class WadBuilder
{
    private sealed record Entry(string Name, byte[] Data);

    private readonly List<Entry> _entries = new();

    /// <summary>All lump names currently staged (in order).</summary>
    public IReadOnlyList<string> Names => _entries.Select(e => e.Name).ToList();

    /// <summary>Stages a named lump (respecting the 8-char WAD name limit).</summary>
    public void AddLump(string name, byte[] data)
    {
        string norm = WadNames.Normalize(name);
        if (norm.Length > 8)
            norm = norm[..8];
        _entries.Add(new Entry(norm, data ?? Array.Empty<byte>()));
    }

    public bool HasLump(string name)
    {
        string norm = WadNames.Normalize(name);
        if (norm.Length > 8)
            norm = norm[..8];
        foreach (var entry in _entries)
        {
            if (entry.Name == norm)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Writes the accumulated lumps as a WAD file.
    /// </summary>
    public void Write(string path, WadType type = WadType.PWad)
    {
        const int HeaderSize = 12;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);

        byte[] magic = Encoding.ASCII.GetBytes(type == WadType.IWad ? "IWAD" : "PWAD");
        stream.Write(magic, 0, 4);
        stream.Write(new byte[8], 0, 8); // placeholder numlumps + dir offset

        Entry[] entries = _entries.ToArray();

        var offsets = new int[entries.Length];
        long cursor = HeaderSize;
        for (int i = 0; i < entries.Length; i++)
        {
            cursor = Align4(cursor);
            offsets[i] = checked((int)cursor);
            WritePaddingZeroes(stream, cursor);
            stream.Write(entries[i].Data, 0, entries[i].Data.Length);
            cursor += entries[i].Data.Length;
        }

        long dirOffset = Align4(cursor);
        WritePaddingZeroes(stream, dirOffset);

        byte[] directory = BuildDirectory(offsets, entries);
        stream.Write(directory, 0, directory.Length);

        // Rewrite the header now that sizes are known.
        stream.Position = 4;
        stream.Write(ToBytes(entries.Length), 0, 4);
        stream.Write(ToBytes(checked((int)dirOffset)), 0, 4);
        stream.Flush();
    }

    // ------------------------------------------------------------------

    private static void WritePaddingZeroes(Stream stream, long targetPosition)
    {
        long current = stream.Position;
        if (targetPosition > current)
        {
            byte[] pad = new byte[targetPosition - current];
            stream.Write(pad, 0, pad.Length);
        }
    }

    private static byte[] BuildDirectory(int[] offsets, Entry[] entries)
    {
        using var ms = new MemoryStream(offsets.Length * 16);
        for (int i = 0; i < entries.Length; i++)
        {
            ms.Write(ToBytes(offsets[i]), 0, 4);
            ms.Write(ToBytes(entries[i].Data.Length), 0, 4);
            byte[] name = Encoding.ASCII.GetBytes(entries[i].Name);
            ms.Write(name, 0, name.Length);
            if (name.Length < 8)
                ms.Write(new byte[8 - name.Length], 0, 8 - name.Length);
        }
        return ms.ToArray();
    }

    private static byte[] ToBytes(int value) => BitConverter.GetBytes(value);

    private static long Align4(long value) => (value + 3) & ~3L;
}