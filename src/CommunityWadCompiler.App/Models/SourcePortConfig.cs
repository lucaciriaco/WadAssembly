using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CommunityWadCompiler.App.Models;

/// <summary>Configured source port (ZDL-style): display name, executable path and
/// optional extra command-line arguments that are prepended to the auto-generated
/// <c>-iwad</c>/<c>-file</c> switches when launching a compiled WAD.</summary>
public sealed class SourcePortConfig : INotifyPropertyChanged
{
    private string _name = "";
    private string _executablePath = "";
    private string _arguments = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set => Set(ref _executablePath, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => Set(ref _arguments, value);
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}