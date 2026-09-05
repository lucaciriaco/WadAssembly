using System.Text;

namespace CommunityWadCompiler.Core.WadFormat;

/// <summary>
/// A parsed WAD archive held fully in memory.
///
/// Layout (little endian):
///   header (12 bytes):   "IWAD"/"PWAD" (4) | numlumps (i32) | infotableofs (i32)
///   lump directory:      16 bytes per entry: filepos (i32) | size (i32) | name (8)
///   lump data:           raw bytes at <c>filepos</c>
/// </summary>
public sealed class WadFile : IDisposable
{
    private readonly byte[] _data;
    private readonly List<Lump> _lumps = new();

    private WadFile(byte[] data, WadType type, string? sourcePath)
    {
        _data = data;
        WadType = type;
        SourcePath = sourcePath;
    }

    /// <summary>WAD content lives in managed memory; disposal is a no-op kept for API symmetry.</summary>
    public void Dispose()
    {
    }

    /// <summary>Path the WAD was opened from, or <c>null</c> for purely in-memory wads.</summary>
    public string? SourcePath { get; }

    /// <summary>Whether this is an IWAD or a PWAD.</summary>
    public WadType WadType { get; }

    /// <summary>All lumps in file order.</summary>
    public IReadOnlyList<Lump> Lumps => _lumps;

    /// <summary>
    /// Opens and parses a WAD file from disk.
    /// </summary>
    /// <exception cref="WadException">Thrown when the file is not a valid WAD.</exception>
    public static WadFile Open(string path)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WadException($"No se pudo leer '{path}': {ex.Message}", path, ex);
        }

        return Parse(data, path);
    }

    /// <summary>Parses a WAD held in a byte array.</summary>
    public static WadFile Parse(byte[] data, string? sourcePath = null)
    {
        if (data.Length < 12)
            throw new WadException("El archivo es demasiado pequeño para ser un WAD.", sourcePath);

        string magic = Encoding.ASCII.GetString(data, 0, 4);
        WadType type = magic switch
        {
            "IWAD" => WadType.IWad,
            "PWAD" => WadType.PWad,
            _ => throw new WadException($"Cabecera de WAD desconocida '{magic}' (se esperaba IWAD o PWAD).", sourcePath),
        };

        int numLumps = BitConverter.ToInt32(data, 4);
        int dirOffset = BitConverter.ToInt32(data, 8);

        if (numLumps < 0 || dirOffset < 0 || dirOffset + numLumps * 16L > data.Length)
            throw new WadException("Directorio de lumps inválido o fuera de rango.", sourcePath);

        var wad = new WadFile(data, type, sourcePath);

        for (int i = 0; i < numLumps; i++)
        {
            int entry = dirOffset + i * 16;
            int filePos = BitConverter.ToInt32(data, entry);
            int size = BitConverter.ToInt32(data, entry + 4);
            string name = ReadLumpName(data, entry + 8);

            if (filePos < 0 || filePos + size > data.Length)
                throw new WadException($"El lump '{name}' apunta fuera del archivo.", sourcePath);

            wad._lumps.Add(new Lump(name, filePos, size, wad));
        }

        return wad;
    }

    /// <summary>Reads the payload of a lump belonging to this WAD.</summary>
    internal byte[] ReadLumpData(Lump lump)
    {
        var result = new byte[lump.Size];
        Array.Copy(_data, lump.DataOffset, result, 0, lump.Size);
        return result;
    }

    /// <summary>Finds the first lump with the given name (case-insensitive) or <c>null</c>.</summary>
    public Lump? FindFirst(string name) => Find(name, last: false);

    /// <summary>Finds the last lump with the given name, mirroring engine "later wad wins".</summary>
    public Lump? FindLast(string name) => Find(name, last: true);

    private Lump? Find(string name, bool last)
    {
        string norm = WadNames.Normalize(name);
        if (last)
        {
            for (int i = _lumps.Count - 1; i >= 0; i--)
                if (string.Equals(_lumps[i].Name, norm, StringComparison.Ordinal))
                    return _lumps[i];
        }
        else
        {
            foreach (var lump in _lumps)
                if (string.Equals(lump.Name, norm, StringComparison.Ordinal))
                    return lump;
        }
        return null;
    }

    /// <summary>Parses the 8-byte lump name field (NUL- or space-padded, ASCII, uppercased).</summary>
    public static string ReadLumpName(byte[] data, int offset)
    {
        Span<byte> raw = data.AsSpan(offset, 8);
        int len = 0;
        while (len < 8 && raw[len] != 0)
            len++;

        var sb = new StringBuilder(len + 1);
        for (int i = 0; i < len; i++)
        {
            char c = (char)raw[i];
            sb.Append(c >= 'a' && c <= 'z' ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }
}