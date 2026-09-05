namespace CommunityWadCompiler.App.ViewModels;

/// <summary>One selectable music option in the music picker: the (possibly renamed) lump
/// name and the WAD it was taken from.</summary>
public sealed record MusicLumpInfo(string Name, string WadPath)
{
    /// <summary>Text shown for the "no music" entry (empty WadPath).</summary>
    public string Display => string.IsNullOrWhiteSpace(WadPath)
        ? Name
        : $"{Name}   [de {Path.GetFileNameWithoutExtension(WadPath)}]";
}