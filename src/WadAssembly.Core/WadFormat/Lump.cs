namespace WadAssembly.Core.WadFormat;

/// <summary>
/// A single WAD lump: a named block of data located inside a <see cref="WadFile"/>.
/// </summary>
public sealed class Lump
{
    internal Lump(string name, int dataOffset, int size, WadFile owner)
    {
        Name = name;
        DataOffset = dataOffset;
        Size = size;
        _owner = owner;
    }

    private readonly WadFile _owner;

    /// <summary>Normalized lump name (uppercase, up to 8 chars, trailing NULs removed).</summary>
    public string Name { get; }

    /// <summary>Absolute offset of the lump data inside the source file.</summary>
    public int DataOffset { get; }

    /// <summary>Size of the lump data in bytes.</summary>
    public int Size { get; }

    /// <summary>Reads the full lump payload into memory.</summary>
    public byte[] ReadAll() => _owner.ReadLumpData(this);

    /// <summary>Reads the first <paramref name="count"/> bytes of the lump payload.</summary>
    public byte[] ReadPrefix(int count)
    {
        if (count <= 0 || Size == 0)
            return Array.Empty<byte>();
        int n = Math.Min(count, Size);
        var result = new byte[n];
        _owner.ReadLumpRange(this, n, result);
        return result;
    }

    public override string ToString() => $"{Name} ({Size} bytes)";
}