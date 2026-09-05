namespace CommunityWadCompiler.Core.WadFormat;

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

    public override string ToString() => $"{Name} ({Size} bytes)";
}