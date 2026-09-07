namespace CommunityWadCompiler.App.ViewModels;

using CommunityWadCompiler.App.Services;

/// <summary>One selectable music option in the music picker: the (possibly renamed) lump
/// name and the WAD it was taken from.</summary>
public sealed record MusicLumpInfo(string Name, string WadPath)
{
    /// <summary>Text shown for the "no music" entry (empty WadPath).</summary>
    public string Display => string.IsNullOrWhiteSpace(WadPath)
        ? Name
        : string.Format(LanguageService.GetString("Music.FromWadFormat"), Name, Path.GetFileNameWithoutExtension(WadPath));
}