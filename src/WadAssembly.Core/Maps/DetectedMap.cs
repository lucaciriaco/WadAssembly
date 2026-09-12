namespace WadAssembly.Core.Maps;

/// <summary>
/// A map found inside a WAD as a contiguous run of lumps starting with a map header.
/// </summary>
public sealed class DetectedMap
{
    public DetectedMap(WadFormat.WadFile wad, int startIndex, int endIndex, bool isUdmf)
    {
        Wad = wad;
        StartIndex = startIndex;
        EndIndex = endIndex;
        IsUdmf = isUdmf;
        OriginalName = wad.Lumps[startIndex].Name;
    }

    /// <summary>WAD that owns this map.</summary>
    public WadFormat.WadFile Wad { get; }

    /// <summary>Index of the map marker lump in <see cref="Wad"/>.</summary>
    public int StartIndex { get; }

    /// <summary>Index of the first lump after the map (exclusive).</summary>
    public int EndIndex { get; }

    /// <summary>Marker name (e.g. "MAP01", "E1M1").</summary>
    public string OriginalName { get; }

    /// <summary>True when the map uses the UDMF format (TEXTMAP/ENDMAP pair).</summary>
    public bool IsUdmf { get; }

    /// <summary>Number of lumps that make up this map (including the marker).</summary>
    public int LumpCount => EndIndex - StartIndex;
}