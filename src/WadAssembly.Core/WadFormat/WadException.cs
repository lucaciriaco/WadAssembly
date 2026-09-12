namespace WadAssembly.Core.WadFormat;

/// <summary>Thrown when a file is not a well-formed WAD archive.</summary>
public sealed class WadException : Exception
{
    public WadException(string message, string? path = null, Exception? inner = null)
        : base(message, inner)
    {
        Path = path;
    }

    /// <summary>Path of the offending file, when known.</summary>
    public string? Path { get; }
}