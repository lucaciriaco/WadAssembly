using System.Text;

namespace CommunityWadCompiler.Core.WadFormat;

/// <summary>Helpers for WAD lump names (max 8 chars, uppercase ASCII).</summary>
public static class WadNames
{
    /// <summary>
    /// Normalizes a lump name for display/comparison: uppercase, trailing NUL/spaces trimmed.
    /// </summary>
    public static string Normalize(string name)
    {
        string upper = name.TrimEnd('\0', ' ').ToUpperInvariant();
        return upper;
    }

    /// <summary>
    /// Encodes a name into the fixed 8-byte WAD field (NUL-padded).
    /// Longer names are truncated to 8 characters.
    /// </summary>
    public static byte[] ToWadField(string name)
    {
        var result = new byte[8];
        string norm = Normalize(name);
        if (norm.Length > 8)
            norm = norm[..8];

        byte[] bytes = Encoding.ASCII.GetBytes(norm);
        Array.Copy(bytes, result, bytes.Length);
        return result;
    }

    /// <summary>Whether the name is a valid patching tool lump name (letters, digits, brackets, dash, underscore).</summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        foreach (char c in name)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') || c is '[' or ']' or '-' or '_';
            if (!ok)
                return false;
        }
        return true;
    }
}