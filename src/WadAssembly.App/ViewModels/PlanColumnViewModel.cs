using WadAssembly.App.Services;

namespace WadAssembly.App.ViewModels;

/// <summary>A logical plan-sheet column: stable id, localized title, width and
/// visibility. Ordering is given by its position in <see cref="PlanColumnsViewModel"/>.</summary>
public sealed class PlanColumnViewModel : ObservableObject
{
    public int Id { get; }

    /// <summary>Resource key used to localize the column title.</summary>
    public string TitleKey { get; }

    private double _width;

    /// <summary>Width in pixels used when the column is visible.</summary>
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    private bool _visible = true;

    /// <summary>False hides the column from the header and every slot row.</summary>
    public bool Visible
    {
        get => _visible;
        set => SetProperty(ref _visible, value);
    }

    /// <summary>Localized title of the column (e.g. "Slot", "Mapa original").</summary>
    public string Title => LanguageService.GetString(TitleKey);

    public PlanColumnViewModel(int id, string titleKey, double width)
    {
        Id = id;
        TitleKey = titleKey;
        _width = width;
    }
}